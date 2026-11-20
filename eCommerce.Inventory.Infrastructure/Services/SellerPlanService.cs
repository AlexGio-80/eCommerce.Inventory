using System.Collections.Concurrent;
using System.Text.Json;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// "Per venditore" (feature 007): per ogni venditore il carrello che conviene comprare da lui con una spedizione
/// sola, con le regole di <see cref="SellerBasketBuilder"/>, e i venditori in ordine di guadagno netto. È il modo in
/// cui compra l'utente: le spedizioni di più venditori si mangiano il guadagno.
///
/// Card Trader: le offerte di tutte le espansioni dei prodotti che interessano, una chiamata per espansione (circa
/// 110 il 17/10, 5–6 minuti con il limite di 20 al minuto), così ogni venditore si vede con tutti i suoi sigillati.
/// Card Trader Zero è un venditore a sé: articoli di venditori diversi in una spedizione. Cardmarket: i venditori
/// incollati. Wizards: i drop acquistabili, in un riquadro a parte con il suo importo.
/// </summary>
public class SellerPlanService
{
    private readonly ApplicationDbContext _db;
    private readonly ICardTraderApiService _cardTrader;
    private readonly BudgetPlanService _budgetPlans;
    private readonly CardmarketSellerOfferService _sellerOffers;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SellerPlanService> _logger;

    public SellerPlanService(
        ApplicationDbContext db,
        ICardTraderApiService cardTrader,
        BudgetPlanService budgetPlans,
        CardmarketSellerOfferService sellerOffers,
        IConfiguration configuration,
        ILogger<SellerPlanService> logger)
    {
        _db = db;
        _cardTrader = cardTrader;
        _budgetPlans = budgetPlans;
        _sellerOffers = sellerOffers;
        _configuration = configuration;
        _logger = logger;
    }

    private decimal Setting(string key, decimal fallback) => _configuration.GetValue($"Purchasing:Budget:{key}", fallback);

    public SellerRules DefaultRules => new(
        Setting("Sellers:AnchorRoiPercent", 50m),
        Setting("Sellers:AddOnRoiPercent", 33m),
        Setting("Sellers:MinNetPercent", 20m),
        Setting("MaxHoldPremiumPercent", 10m));

    /// <summary>Le offerte di tutti i venditori. La parte lenta: <paramref name="progress"/> riceve espansioni lette e totale.</summary>
    public async Task<SellerPlanData> CollectAsync(IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        // Prodotti che possono entrare in un carrello: da aprire con una resa almeno discreta al trend (dal venditore il
        // prezzo può essere più basso) o candidati da tenere.
        var minRoiToRead = Setting("Sellers:MinRoiToReadPercent", 5m);
        var rows = await _budgetPlans.LoadOpportunityRowsAsync(cancellationToken);
        var relevant = rows
            .Where(r => (r.Decision == "Apri" && r.OpenValueCm > 0 && r.RoiPercent >= minRoiToRead) || r.IsHoldCandidate)
            .GroupBy(r => r.ProductId).Select(g => g.First())
            .ToList();
        // Tutti i prodotti della classifica: un venditore Cardmarket incollato si valuta su tutto quello che ha, al suo
        // prezzo. Card Trader invece si legge solo per i prodotti che interessano (una chiamata per espansione).
        var cmNames = await _db.CardmarketProducts.AsNoTracking()
            .Where(p => !p.IsSingle)
            .ToDictionaryAsync(p => p.IdProduct, p => p.Name, cancellationToken);
        var products = rows.GroupBy(r => r.ProductId).Select(g => g.First()).ToDictionary(r => r.ProductId, r => new SellerProduct(
            r.ProductId, r.Name, r.SetCode, r.Decision, r.OpenValueCm, r.CmTrend, r.HoldScore, r.IsHoldCandidate,
            r.CardTraderBlueprintId, r.CardmarketId is { } id ? cmNames.GetValueOrDefault(id) : null));
        var toRead = relevant.Select(r => products[r.ProductId]).ToList();

        var offers = new List<SellerOffer>();
        var (expansionsRead, failures) = await AddCardTraderAsync(toRead, products.Values.ToList(), offers, progress, cancellationToken);

        var wizards = await _budgetPlans.GetBuyableSecretLairAsync(false, cancellationToken);

        _logger.LogInformation(
            "Venditori: {Products} prodotti letti su Card Trader, {Expansions} espansioni lette ({Failures} non riuscite), {Offers} offerte, {Wizards} drop Wizards",
            toRead.Count, expansionsRead, failures, offers.Count, wizards.Count);

        return WithCardmarketOffers(new SellerPlanData(DateTime.UtcNow, products, offers, wizards, expansionsRead, failures),
            await _sellerOffers.GetValidOffersAsync(cancellationToken));
    }

    /// <summary>Le offerte lette, con quelle Cardmarket sostituite dai venditori incollati validi adesso.</summary>
    public static SellerPlanData WithCardmarketOffers(SellerPlanData data, IEnumerable<ValidSellerOffer> pasted) =>
        data with
        {
            Offers = data.Offers.Where(o => o.Store != "Cardmarket")
                .Concat(pasted.Where(o => data.Products.ContainsKey(o.SealedProductId)).Select(o =>
                    new SellerOffer("Cardmarket", o.Seller, null, o.SealedProductId, o.Price, o.Quantity, false, null, o.PastedAt, o.Shipping)))
                .ToList()
        };

    /// <param name="toRead">I prodotti che interessano: decidono quali espansioni leggere.</param>
    /// <param name="all">
    /// Tutti i prodotti della classifica: di un'espansione letta si tengono le offerte di tutti, perché la risposta li
    /// contiene già. Così la scheda di un venditore mostra anche cos'altro ha da aggiungere alla stessa spedizione
    /// (richiesta dell'utente del 20/11), e il suo carrello si valuta su tutto, come per i venditori Cardmarket incollati.
    /// </param>
    private async Task<(int Read, int Failures)> AddCardTraderAsync(
        List<SellerProduct> toRead, List<SellerProduct> all, List<SellerOffer> offers, IProgress<(int, int)>? progress,
        CancellationToken cancellationToken)
    {
        var productByBlueprint = all
            .Where(p => p.CardTraderBlueprintId != null)
            .GroupBy(p => p.CardTraderBlueprintId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ProductId);
        var blueprintIds = toRead.Where(p => p.CardTraderBlueprintId != null).Select(p => p.CardTraderBlueprintId!.Value).Distinct().ToList();
        var expansions = await _db.Blueprints.AsNoTracking()
            .Where(b => blueprintIds.Contains(b.CardTraderId))
            .Select(b => b.Expansion.CardTraderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var done = 0;
        var failures = 0;
        progress?.Report((0, expansions.Count));
        foreach (var expansionId in expansions)
        {
            try
            {
                var marketplace = await _cardTrader.GetMarketplaceProductsByExpansionAsync(expansionId, cancellationToken);
                foreach (var o in marketplace)
                {
                    if (!productByBlueprint.TryGetValue(o.BlueprintId, out var productId)) continue;
                    if (ToOffer(o, productId) is { } offer) offers.Add(offer);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un'espansione che non risponde non ferma le altre: i suoi venditori mancheranno da questa lettura.
                failures++;
                _logger.LogWarning(ex, "Venditori: offerte dell'espansione Card Trader {ExpansionId} non lette", expansionId);
            }
            progress?.Report((++done, expansions.Count));
        }
        return (done - failures, failures);
    }

    /// <summary>Carrelli per venditore sulle offerte già lette: in millisecondi.</summary>
    /// <param name="history">Acquisti già fatti per prodotto (registro): i non ripagati non si ricomprano da aprire.</param>
    public static SellerPlanResult Compose(SellerPlanData data, SellerPlanRequest request,
        IReadOnlyDictionary<int, PurchaseHistory>? history = null)
    {
        if (history != null)
            data = data with { Products = data.Products.ToDictionary(kv => kv.Key, kv => kv.Value with { History = history.GetValueOrDefault(kv.Key) }) };
        var rules = request.Rules;
        var shipping = request.Shipping;
        var stores = request.ExcludedStores?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        var baskets = new List<SellerBasket>();

        // Card Trader: ogni venditore con la sua spedizione, quelli CT Zero anche da soli (comprando solo da loro).
        if (!stores.Contains("CardTrader"))
        {
            foreach (var g in data.Offers.Where(o => o.Store != "Cardmarket").GroupBy(o => o.Seller))
            {
                var direct = g.Select(o => o with { Store = "Card Trader" }).ToList();
                if (SellerBasketBuilder.Build($"ct:{g.Key}", direct, data.Products, shipping.CardTrader, rules,
                        maxTotal: request.Budget) is { } b) baskets.Add(b);
            }
            // Card Trader Zero: un solo carrello con l'offerta più economica di ogni prodotto, fra tutti i venditori CT Zero.
            var zero = data.Offers.Where(o => o.CtZero).Select(o => o with { Store = "Card Trader Zero" }).ToList();
            if (zero.Count > 0 && SellerBasketBuilder.Build("ctzero", zero, data.Products, shipping.CardTraderZero, rules,
                    basketSeller: "più venditori, una spedizione", maxTotal: request.Budget) is { } z) baskets.Add(z);
        }
        if (!stores.Contains("Cardmarket"))
        {
            foreach (var g in data.Offers.Where(o => o.Store == "Cardmarket").GroupBy(o => o.Seller))
            {
                if (SellerBasketBuilder.Build($"cm:{g.Key}", g.ToList(), data.Products, shipping.Cardmarket, rules,
                        maxTotal: request.Budget) is { } b) baskets.Add(b);
            }
        }

        var ranked = SellerBasketBuilder.RankAndFit(baskets, request.Budget);

        // I prodotti lasciati fuori perché non ripagati, fra quelli che qualcuno vende a una resa da aprire.
        var notPaidBack = data.Offers
            .Where(o => data.Products.TryGetValue(o.ProductId, out var p) && SellerBasketBuilder.IsNotPaidBack(p)
                && SellerBasketBuilder.RoiAt(p, o.Price) >= rules.AddOnRoiPercent)
            .Select(o => data.Products[o.ProductId])
            .DistinctBy(p => p.ProductId)
            .Select(p => new NotPaidBackProduct(p.ProductId, p.Name, p.SetCode, p.History!.SoldPercent))
            .OrderBy(p => p.Name)
            .ToList();

        // Wizards a parte: i drop col margine migliore finché stanno nell'importo, spedizione una volta.
        var wizardsLeft = request.WizardsBudget - shipping.Wizards;
        var drops = data.Wizards
            .OrderByDescending(d => d.MarginPercent ?? 0)
            .Select(d =>
            {
                var chosen = !stores.Contains("Wizards") && d.Price <= wizardsLeft;
                if (chosen) wizardsLeft -= d.Price;
                return new WizardsChoice(d, chosen);
            })
            .ToList();
        var chosenDrops = drops.Where(d => d.Chosen).ToList();

        return new SellerPlanResult(data.CollectedAt, request, ranked, ranked.Count(b => b.InBudget),
            Math.Round(ranked.Where(b => b.InBudget).Sum(b => b.Total), 2), Math.Round(ranked.Where(b => b.InBudget).Sum(b => b.OpenGain), 2),
            drops, Math.Round(chosenDrops.Sum(d => d.Drop.Price) + (chosenDrops.Count > 0 ? shipping.Wizards : 0), 2),
            Math.Round(chosenDrops.Sum(d => d.Drop.NetValue - d.Drop.Price) - (chosenDrops.Count > 0 ? shipping.Wizards : 0), 2),
            data.Products.Count, data.ExpansionsRead, data.ExpansionFailures,
            data.Offers.Where(o => o.Store == "Cardmarket").Select(o => o.Seller).Distinct().Count(), notPaidBack);
    }

    /// <summary>
    /// Tutte le offerte lette di un venditore Card Trader, una per prodotto (la più economica), valutate con le regole
    /// della schermata: quelle del suo carrello, quelle convenienti rimaste fuori (budget o netto), quelle di possibile
    /// interesse (rendono qualcosa, o da tenere ma care) e il resto. Null se il venditore non ha offerte lette.
    /// </summary>
    public static CardTraderSellerOffers? SellerOffers(SellerPlanData data, SellerPlanRequest request, string seller,
        IReadOnlyDictionary<int, PurchaseHistory>? history = null)
    {
        var products = history == null ? data.Products
            : data.Products.ToDictionary(kv => kv.Key, kv => kv.Value with { History = history.GetValueOrDefault(kv.Key) });
        var direct = data.Offers
            .Where(o => o.Store != "Cardmarket" && o.Seller == seller && products.ContainsKey(o.ProductId))
            .Select(o => o with { Store = "Card Trader" })
            .ToList();
        if (direct.Count == 0) return null;

        var rules = request.Rules;
        var basket = SellerBasketBuilder.Build($"ct:{seller}", direct, products, request.Shipping.CardTrader, rules, maxTotal: request.Budget);
        var inBasket = basket?.Lines.ToDictionary(l => l.Offer.ProductId, l => l.Kind) ?? new Dictionary<int, SellerLineKind>();

        var rows = direct
            .GroupBy(o => o.ProductId)
            .Select(g =>
            {
                var offer = g.OrderBy(o => o.Price).First() with { Available = g.Sum(o => o.Available) };
                var p = products[offer.ProductId];
                var roi = SellerBasketBuilder.RoiAt(p, offer.Price);
                var gain = roi != null ? p.OpenValueNet!.Value - offer.Price : (decimal?)null;
                var (group, verdict) =
                    inBasket.TryGetValue(offer.ProductId, out var kind) ? (0, kind == SellerLineKind.Open ? "Nel carrello: da aprire" : "Nel carrello: da tenere")
                    : Judge(p, offer.Price, rules,
                        basket == null
                            ? "Da aprire, ma senza un traino questo venditore non fa un carrello"
                            : "Da aprire, fuori dal carrello: non ci sta nel budget o abbassa il netto sotto la soglia",
                        basket == null
                            ? "Da tenere, ma senza un'apertura questo venditore non fa un carrello"
                            : "Da tenere, fuori dal carrello: non ci sta nel budget");
                return new CardTraderSellerOffer(offer, p, roi is { } r ? Math.Round(r, 1) : null, gain is { } x ? Math.Round(x, 2) : null,
                    group == 0, group, verdict);
            })
            .OrderBy(r => r.Group)
            .ThenByDescending(r => r.RoiPercent ?? -999)
            .ToList();

        return new CardTraderSellerOffers(seller, direct[0].Country, data.Offers.Any(o => o.Seller == seller && o.CtZero), data.CollectedAt, rows);
    }

    /// <summary>Oltre questa resa è quasi sempre un prezzo sbagliato, come negli avvisi.</summary>
    private const decimal MaxRealisticRoi = 300m;

    private static readonly System.Globalization.CultureInfo Italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Un'offerta Card Trader utilizzabile (in inglese, disponibile, venditore non in vacanza); null se no.</summary>
    public static SellerOffer? ToOffer(CardTraderMarketplaceProductDto o, int productId) =>
        o.PriceCents <= 0 || o.Quantity <= 0 || o.OnVacation || !IsEnglish(o.Properties.Language) ? null
            : new SellerOffer(o.User.CanSellSealedWithCtZero ? "Card Trader Zero" : "Card Trader", o.User.Username,
                o.User.CountryCode, productId, o.PriceCents / 100m, o.Quantity, o.User.CanSellSealedWithCtZero, o.Id, null);

    /// <summary>
    /// Valutazione di un'offerta rimasta fuori da un ordine: 1 conveniente, 2 di possibile interesse, 3 il resto.
    /// </summary>
    private static (int Group, string Verdict) Judge(SellerProduct p, decimal price, SellerRules rules, string openOutside, string holdOutside)
    {
        var roi = SellerBasketBuilder.RoiAt(p, price);
        var holdOk = p.IsHoldCandidate && p.CmTrend is > 0 && price <= p.CmTrend.Value * (1 + rules.MaxHoldPremiumPercent / 100);
        return roi > MaxRealisticRoi ? (3, "Prezzo da controllare: resa troppo alta per essere vera")
            : roi >= rules.AddOnRoiPercent && SellerBasketBuilder.IsNotPaidBack(p)
                ? (2, string.Format(Italian, "Da aprire, ma già comprato e non ancora ripagato (venduto {0:0}%)", p.History!.SoldPercent ?? 0))
            : roi >= rules.AddOnRoiPercent ? (1, openOutside)
            : holdOk ? (1, holdOutside)
            : p.IsHoldCandidate && p.CmTrend is > 0
                ? (2, string.Format(Italian, "Da tenere, ma caro: {0:0}% sopra il trend", (price / p.CmTrend.Value - 1) * 100))
            : roi > 0 ? (2, string.Format(Italian, "Rende il {0:0}%, sotto la soglia \"altri\" ({1:0}%)", roi, rules.AddOnRoiPercent))
            : roi != null ? (3, "Aprirlo non rende a questo prezzo")
            : (3, p.Decision is { } d ? $"Nessuna resa affidabile: {d}" : "Nessuna valutazione oggi");
    }

    /// <summary>
    /// Chi vende un prodotto che l'utente vuole comprare comunque, qualunque sia la resa (richiesta del 20/11): per ogni
    /// venditore Card Trader, per il carrello Card Trader Zero e per i venditori Cardmarket incollati, l'ordine con quel
    /// prodotto più le sue aperture e i suoi tenuti secondo le regole della schermata, dentro il budget (o il solo
    /// prodotto, se da solo lo supera). In cima chi costa meno contando il guadagno atteso delle aggiunte: un venditore un
    /// po' più caro con altro da rivendere può convenire di più. Null se il prodotto non è nella lettura.
    /// </summary>
    /// <param name="freshCardTrader">Offerte Card Trader del prodotto lette adesso; null = quelle della lettura.</param>
    public static ProductSellers? ForProduct(SellerPlanData data, SellerPlanRequest request, int productId,
        IReadOnlyList<SellerOffer>? freshCardTrader, IReadOnlyDictionary<int, PurchaseHistory>? history = null)
    {
        var products = history == null ? data.Products
            : data.Products.ToDictionary(kv => kv.Key, kv => kv.Value with { History = history.GetValueOrDefault(kv.Key) });
        if (!products.TryGetValue(productId, out var product)) return null;

        var offers = freshCardTrader == null ? data.Offers
            : data.Offers.Where(o => !(o.Store != "Cardmarket" && o.ProductId == productId)).Concat(freshCardTrader).ToList();
        var target = offers.Where(o => o.ProductId == productId).ToList();
        var rules = request.Rules;
        var orders = new List<ProductSellerOrder>();

        foreach (var seller in target.Where(o => o.Store != "Cardmarket").Select(o => o.Seller).Distinct())
        {
            var own = offers.Where(o => o.Store != "Cardmarket" && o.Seller == seller).Select(o => o with { Store = "Card Trader" }).ToList();
            if (BuildOrder($"ct:{seller}", "Card Trader", seller, own[0].Country, own, productId, products, request.Shipping.CardTrader, rules,
                    request.Budget, allOthers: true) is { } order) orders.Add(order);
        }
        if (target.Any(o => o.CtZero))
        {
            var zero = offers.Where(o => o.CtZero).Select(o => o with { Store = "Card Trader Zero" }).ToList();
            if (BuildOrder("ctzero", "Card Trader Zero", "più venditori, una spedizione", null, zero, productId, products,
                    request.Shipping.CardTraderZero, rules, request.Budget, allOthers: false) is { } order) orders.Add(order);
        }
        foreach (var seller in target.Where(o => o.Store == "Cardmarket").Select(o => o.Seller).Distinct())
        {
            var own = offers.Where(o => o.Store == "Cardmarket" && o.Seller == seller).ToList();
            if (BuildOrder($"cm:{seller}", "Cardmarket", seller, null, own, productId, products, request.Shipping.Cardmarket, rules,
                    request.Budget, allOthers: true) is { } order) orders.Add(order);
        }

        return new ProductSellers(product, data.CollectedAt, freshCardTrader != null, rules, request.Budget,
            orders.OrderBy(o => o.EffectiveCost).ThenBy(o => o.Offer.Price).ToList());
    }

    /// <param name="allOthers">False per Card Trader Zero: fra tutti i suoi venditori si mostrano solo le altre offerte interessanti.</param>
    private static ProductSellerOrder? BuildOrder(string key, string store, string seller, string? country, List<SellerOffer> offers,
        int productId, IReadOnlyDictionary<int, SellerProduct> products, decimal defaultShipping, SellerRules rules, decimal budget,
        bool allOthers)
    {
        var cheapest = offers
            .Where(o => o.Price > 0 && products.ContainsKey(o.ProductId))
            .GroupBy(o => o.ProductId)
            .Select(g => g.OrderBy(o => o.Price).First() with { Available = g.Sum(o => o.Available) })
            .ToList();
        var target = cheapest.FirstOrDefault(o => o.ProductId == productId);
        if (target == null) return null;

        decimal Ship(SellerOffer o) => o.Shipping ?? defaultShipping;
        var shipping = Ship(target);
        var items = target.Price;
        var cap = Math.Max(budget, items + shipping);

        var open = new List<SellerBasketLine>();
        var hold = new List<SellerBasketLine>();
        foreach (var o in cheapest.Where(o => o != target))
        {
            var p = products[o.ProductId];
            var roi = SellerBasketBuilder.RoiAt(p, o.Price);
            if (roi is { } r && r >= rules.AddOnRoiPercent && r <= MaxRealisticRoi && !SellerBasketBuilder.IsNotPaidBack(p))
                open.Add(new SellerBasketLine(o, p, SellerLineKind.Open, Math.Round(r, 1), Math.Round(p.OpenValueNet!.Value - o.Price, 2)));
            else if (p.IsHoldCandidate && p.CmTrend is > 0 && o.Price <= p.CmTrend.Value * (1 + rules.MaxHoldPremiumPercent / 100))
                hold.Add(new SellerBasketLine(o, p, SellerLineKind.Hold, roi is { } h ? Math.Round(h, 1) : null, null));
        }

        var chosen = new List<SellerBasketLine>();
        foreach (var l in open.OrderByDescending(l => l.RoiPercent).Concat(hold.OrderByDescending(l => l.Product.HoldScore)))
        {
            var withShipping = Math.Max(shipping, Ship(l.Offer));
            if (items + l.Offer.Price + withShipping > cap) continue;
            chosen.Add(l);
            items += l.Offer.Price;
            shipping = withShipping;
        }

        var chosenIds = chosen.Select(l => l.Offer.ProductId).ToHashSet();
        var others = cheapest
            .Where(o => o != target && !chosenIds.Contains(o.ProductId))
            .Select(o =>
            {
                var p = products[o.ProductId];
                var (group, verdict) = Judge(p, o.Price, rules,
                    "Da aprire, fuori dall'ordine: non ci sta nel budget", "Da tenere, fuori dall'ordine: non ci sta nel budget");
                var roi = SellerBasketBuilder.RoiAt(p, o.Price);
                return new CardTraderSellerOffer(o, p, roi is { } r ? Math.Round(r, 1) : null,
                    roi != null ? Math.Round(p.OpenValueNet!.Value - o.Price, 2) : null, false, group, verdict);
            })
            .Where(o => allOthers || o.Group <= 2)
            .OrderBy(o => o.Group)
            .ThenByDescending(o => o.RoiPercent ?? -999)
            .ToList();

        var addOnGain = chosen.Sum(l => l.Gain ?? 0);
        var targetRoi = SellerBasketBuilder.RoiAt(products[productId], target.Price);
        return new ProductSellerOrder(key, store, seller, country, target, targetRoi is { } t ? Math.Round(t, 1) : null, shipping,
            chosen, chosen.Sum(l => l.Offer.Price), Math.Round(addOnGain, 2),
            Math.Round(target.Price + shipping - addOnGain, 2), Math.Round(items + shipping, 2), others);
    }

    private static bool IsEnglish(string? language) =>
        string.IsNullOrEmpty(language)
        || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "English", StringComparison.OrdinalIgnoreCase);
}

/// <param name="Budget">Budget per i venditori Card Trader e Cardmarket.</param>
/// <param name="WizardsBudget">Importo per il negozio Wizards, a parte.</param>
/// <param name="ExcludedStores">"CardTrader", "Cardmarket", "Wizards".</param>
public record SellerPlanRequest(decimal Budget, decimal WizardsBudget, SellerRules Rules, BudgetShipping Shipping, List<string>? ExcludedStores);

public record SellerPlanData(
    DateTime CollectedAt, Dictionary<int, SellerProduct> Products, List<SellerOffer> Offers, List<WizardsDrop> Wizards,
    int ExpansionsRead, int ExpansionFailures);

/// <summary>Chi vende un prodotto, con l'ordine che conviene fare da ognuno (finestra "Chi lo vende").</summary>
/// <param name="FreshCardTrader">Le offerte Card Trader del prodotto sono state lette adesso.</param>
public record ProductSellers(SellerProduct Product, DateTime CollectedAt, bool FreshCardTrader, SellerRules Rules, decimal Budget,
    List<ProductSellerOrder> Orders);

/// <param name="Offer">L'offerta del prodotto cercato.</param>
/// <param name="AddOns">Aperture e tenuti dello stesso venditore aggiunti con le regole della schermata.</param>
/// <param name="EffectiveCost">Prezzo del prodotto + spedizione - guadagno atteso delle aggiunte: quanto costa davvero prenderlo da qui.</param>
/// <param name="Others">Le altre sue offerte rimaste fuori, valutate (gruppo 1 conveniente, 2 possibile interesse, 3 il resto).</param>
public record ProductSellerOrder(
    string Key, string Store, string Seller, string? Country, SellerOffer Offer, decimal? TargetRoiPercent, decimal Shipping,
    List<SellerBasketLine> AddOns, decimal AddOnCost, decimal AddOnGain, decimal EffectiveCost, decimal Total,
    List<CardTraderSellerOffer> Others);

/// <summary>Le offerte lette di un venditore Card Trader, valutate (scheda del venditore in "Per venditore").</summary>
public record CardTraderSellerOffers(string Seller, string? Country, bool CtZero, DateTime CollectedAt, List<CardTraderSellerOffer> Offers);

/// <param name="Group">0 nel carrello, 1 conveniente ma fuori, 2 di possibile interesse, 3 il resto.</param>
public record CardTraderSellerOffer(
    SellerOffer Offer, SellerProduct Product, decimal? RoiPercent, decimal? Gain, bool InBasket, int Group, string Verdict);

public record WizardsChoice(WizardsDrop Drop, bool Chosen);

/// <param name="InBudgetCount">Venditori che stanno nel budget, presi in ordine di guadagno.</param>
public record SellerPlanResult(
    DateTime CollectedAt, SellerPlanRequest Request, List<SellerBasket> Sellers, int InBudgetCount, decimal InBudgetTotal,
    decimal InBudgetGain, List<WizardsChoice> Wizards, decimal WizardsTotal, decimal WizardsGain, int Products,
    int ExpansionsRead, int ExpansionFailures, int CardmarketSellers, List<NotPaidBackProduct>? NotPaidBack = null);

/// <summary>Prodotto già comprato e non ancora ripagato, lasciato fuori dalle aperture.</summary>
public record NotPaidBackProduct(int ProductId, string Name, string SetCode, decimal? SoldPercent);

/// <summary>
/// Lettura per venditore in background, come <see cref="BudgetPlanRunner"/>: una alla volta, risultati in memoria
/// qualche ora, ricalcolo immediato con altre soglie, budget o negozi.
/// </summary>
public class SellerPlanRunner
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SellerPlanRunner> _logger;
    private readonly ConcurrentDictionary<string, Run> _runs = new();
    private readonly object _startLock = new();

    public SellerPlanRunner(IServiceScopeFactory scopeFactory, ILogger<SellerPlanRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public SellerPlanStatus Start(SellerPlanRequest request)
    {
        lock (_startLock)
        {
            // Si tolgono le letture vecchie, tranne l'ultima riuscita: è quella che la scheda riapre.
            var latest = _runs.Values.Where(r => r.State == "done").OrderByDescending(r => r.StartedAt).FirstOrDefault();
            foreach (var old in _runs.Values.Where(r => r.State != "running" && r != latest && DateTime.UtcNow - r.StartedAt > KeepFor).ToList())
                _runs.TryRemove(old.Id, out _);
            var running = _runs.Values.FirstOrDefault(r => r.State == "running");
            if (running != null) return running.Status();

            var run = new Run(Guid.NewGuid().ToString("N")[..12], request);
            _runs[run.Id] = run;
            _ = Task.Run(() => ExecuteAsync(run));
            return run.Status();
        }
    }

    public SellerPlanStatus? Get(string id) => _runs.TryGetValue(id, out var run) ? run.Status() : null;

    /// <summary>
    /// L'ultima lettura riuscita, per ricalcolare senza rileggere Card Trader: quella in memoria o, dopo un riavvio, quella
    /// salvata nel database, ricalcolata con le offerte Cardmarket incollate di adesso.
    /// </summary>
    public async Task<SellerPlanStatus?> LatestAsync(CancellationToken cancellationToken)
    {
        var inMemory = _runs.Values.Where(r => r.State == "done").OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (inMemory != null) return inMemory.Status();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = await db.SellerPlanSnapshots.AsNoTracking().OrderByDescending(x => x.CollectedAt).FirstOrDefaultAsync(cancellationToken);
        if (snapshot == null) return null;

        try
        {
            var request = JsonSerializer.Deserialize<SellerPlanRequest>(snapshot.RequestJson)!;
            var data = JsonSerializer.Deserialize<SellerPlanData>(snapshot.DataJson)!;
            var sellerOffers = scope.ServiceProvider.GetRequiredService<CardmarketSellerOfferService>();
            data = SellerPlanService.WithCardmarketOffers(data, await sellerOffers.GetValidOffersAsync(cancellationToken));
            var run = new Run(snapshot.RunId, request, data.CollectedAt) { Data = data, State = "done" };
            run.Result = SellerPlanService.Compose(data, request, await HistoryAsync(scope, cancellationToken));
            _runs.TryAdd(run.Id, run);
            _logger.LogInformation("Venditori: ripresa la lettura salvata del {CollectedAt}", data.CollectedAt);
            return _runs[run.Id].Status();
        }
        catch (JsonException ex)
        {
            // Una lettura salvata da una versione con un'altra forma dei dati: meglio rifarla che mostrarla sbagliata.
            _logger.LogWarning(ex, "Venditori: lettura salvata non leggibile, va rifatta");
            return null;
        }
    }

    /// <summary>
    /// Ricalcola sulle offerte già lette. Le offerte Cardmarket si rileggono dal database, perché costa poco e un
    /// venditore incollato dopo la ricerca deve entrare senza rileggere Card Trader.
    /// </summary>
    public async Task<SellerPlanStatus?> RecomputeAsync(string id, SellerPlanRequest request, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(id, out var run) || run.Data == null) return null;
        using var scope = _scopeFactory.CreateScope();
        var sellerOffers = scope.ServiceProvider.GetRequiredService<CardmarketSellerOfferService>();
        run.Data = SellerPlanService.WithCardmarketOffers(run.Data, await sellerOffers.GetValidOffersAsync(cancellationToken));
        run.Result = SellerPlanService.Compose(run.Data, request, await HistoryAsync(scope, cancellationToken));
        return run.Status();
    }

    /// <summary>
    /// Chi vende un prodotto: le sue offerte Card Trader lette adesso (una chiamata), gli altri prodotti di ogni venditore
    /// dall'ultima lettura, i venditori Cardmarket incollati. Null se non c'è una lettura o il prodotto non vi compare.
    /// </summary>
    public async Task<ProductSellers?> ProductSellersAsync(int productId, CancellationToken cancellationToken)
    {
        await LatestAsync(cancellationToken);
        var run = _runs.Values.Where(r => r.State == "done" && r.Data != null).OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (run?.Data == null) return null;

        using var scope = _scopeFactory.CreateScope();
        var sellerOffers = scope.ServiceProvider.GetRequiredService<CardmarketSellerOfferService>();
        var data = SellerPlanService.WithCardmarketOffers(run.Data, await sellerOffers.GetValidOffersAsync(cancellationToken));

        List<SellerOffer>? fresh = null;
        if (data.Products.GetValueOrDefault(productId)?.CardTraderBlueprintId is { } blueprintId)
        {
            try
            {
                var api = scope.ServiceProvider.GetRequiredService<ICardTraderApiService>();
                fresh = (await api.GetMarketplaceProductsAsync(blueprintId, cancellationToken))
                    .Where(o => o.BlueprintId == blueprintId)
                    .Select(o => SellerPlanService.ToOffer(o, productId))
                    .OfType<SellerOffer>()
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Senza la lettura di adesso restano le offerte della lettura salvata: meglio vecchie che niente.
                _logger.LogWarning(ex, "Chi lo vende: offerte Card Trader del blueprint {BlueprintId} non lette, uso la lettura salvata", blueprintId);
            }
        }

        return SellerPlanService.ForProduct(data, run.Result?.Request ?? run.Request, productId, fresh, await HistoryAsync(scope, cancellationToken));
    }

    /// <summary>Tutte le offerte di un venditore Card Trader nella lettura, con le regole dell'ultimo calcolo.</summary>
    public async Task<CardTraderSellerOffers?> CardTraderSellerAsync(string id, string seller, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(id, out var run) || run.Data == null) return null;
        using var scope = _scopeFactory.CreateScope();
        return SellerPlanService.SellerOffers(run.Data, run.Result?.Request ?? run.Request, seller, await HistoryAsync(scope, cancellationToken));
    }

    private async Task ExecuteAsync(Run run)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SellerPlanService>();
            var progress = new Progress<(int Done, int Total)>(p => { run.Done = p.Done; run.Total = p.Total; });
            run.Data = await service.CollectAsync(progress, CancellationToken.None);
            run.Result = SellerPlanService.Compose(run.Data, run.Request, await HistoryAsync(scope, CancellationToken.None));
            run.State = "done";
            await SaveAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), run);
            _logger.LogInformation("Venditori {Id}: {Sellers} venditori convenienti, {InBudget} nel budget", run.Id, run.Result.Sellers.Count, run.Result.InBudgetCount);
        }
        catch (Exception ex)
        {
            run.State = "failed";
            run.Error = "Lettura dei venditori non riuscita: " + ex.Message;
            _logger.LogError(ex, "Venditori {Id}: lettura fallita", run.Id);
        }
    }

    /// <summary>Gli acquisti dal registro, letti a ogni calcolo: un acquisto appena registrato conta subito.</summary>
    private static Task<Dictionary<int, PurchaseHistory>> HistoryAsync(IServiceScope scope, CancellationToken cancellationToken) =>
        scope.ServiceProvider.GetRequiredService<ProductPurchaseService>().HistoryByProductAsync(cancellationToken);

    /// <summary>Salva la lettura appena finita al posto della precedente; un errore qui non fa fallire la lettura.</summary>
    private async Task SaveAsync(ApplicationDbContext db, Run run)
    {
        try
        {
            db.SellerPlanSnapshots.RemoveRange(await db.SellerPlanSnapshots.ToListAsync());
            db.SellerPlanSnapshots.Add(new SellerPlanSnapshot
            {
                RunId = run.Id,
                CollectedAt = run.Data!.CollectedAt,
                RequestJson = JsonSerializer.Serialize(run.Request),
                DataJson = JsonSerializer.Serialize(run.Data)
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Venditori {Id}: lettura non salvata, dopo un riavvio andrà rifatta", run.Id);
        }
    }

    private sealed class Run
    {
        public Run(string id, SellerPlanRequest request, DateTime? startedAt = null)
        {
            Id = id;
            Request = request;
            StartedAt = startedAt ?? DateTime.UtcNow;
        }

        public string Id { get; }
        public SellerPlanRequest Request { get; }
        public DateTime StartedAt { get; }
        public volatile string State = "running";
        public volatile int Done;
        public volatile int Total;
        public string? Error;
        public SellerPlanData? Data;
        public SellerPlanResult? Result;

        public SellerPlanStatus Status() => new(Id, State, Done, Total, StartedAt, Error, Result);
    }
}

/// <param name="Done">Espansioni Card Trader lette.</param>
public record SellerPlanStatus(string Id, string State, int Done, int Total, DateTime StartedAt, string? Error, SellerPlanResult? Result);
