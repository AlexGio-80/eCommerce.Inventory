using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Pianificatore del budget (feature 007, Fase 0): raccoglie i candidati della classifica di oggi da aprire e
/// da tenere, le loro offerte (Card Trader per venditore, Cardmarket al prezzo più basso del listino) e i
/// Secret Lair in vendita su Wizards, poi lascia a <see cref="BudgetPlanner"/> la scelta dentro il budget.
///
/// La lettura delle offerte Card Trader è la parte lenta (una chiamata per prodotto, nel limite di 20 al
/// minuto): si fa una volta, e <see cref="Compose"/> ricalcola il piano sulle stesse offerte quando l'utente
/// toglie una riga o cambia le spedizioni.
/// </summary>
public class BudgetPlanService
{
    /// <summary>Come il piano d'acquisto Card Trader: oltre, la lettura delle offerte dura troppi minuti.</summary>
    private const int DefaultMaxCandidates = 40;

    /// <summary>Offerte Card Trader tenute per prodotto: le più economiche bastano a comporre i carrelli.</summary>
    private const int OffersPerProduct = 6;

    /// <summary>Sotto questo numero di copie fra vendute e in vendita, la quota venduta di un'uscita non dice niente.</summary>
    private const int MinSellThroughCopies = 200;

    private static readonly TimeSpan OfferMaxAge = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _db;
    private readonly ICardTraderApiService _cardTrader;
    private readonly IMarketplaceOfferCache _offerCache;
    private readonly SecretLairShopMonitorService _secretLairShop;
    private readonly SecretLairDropValuationService _secretLairValuation;
    private readonly CardmarketSellerOfferService _sellerOffers;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BudgetPlanService> _logger;

    public BudgetPlanService(
        ApplicationDbContext db,
        ICardTraderApiService cardTrader,
        IMarketplaceOfferCache offerCache,
        SecretLairShopMonitorService secretLairShop,
        SecretLairDropValuationService secretLairValuation,
        CardmarketSellerOfferService sellerOffers,
        IConfiguration configuration,
        ILogger<BudgetPlanService> logger)
    {
        _db = db;
        _cardTrader = cardTrader;
        _offerCache = offerCache;
        _secretLairShop = secretLairShop;
        _secretLairValuation = secretLairValuation;
        _sellerOffers = sellerOffers;
        _configuration = configuration;
        _logger = logger;
    }

    private decimal Setting(string key, decimal fallback) => _configuration.GetValue($"Purchasing:Budget:{key}", fallback);

    /// <summary>Spedizioni predefinite, generiche: quelle vere dell'utente stanno nella configurazione di produzione.</summary>
    public BudgetShipping DefaultShipping => new(
        Setting("Shipping:CardTrader", 7m),
        Setting("Shipping:CardTraderZero", 8m),
        Setting("Shipping:Cardmarket", 7m),
        Setting("Shipping:Wizards", 10m));

    /// <summary>Candidati e offerte. La parte lenta: <paramref name="progress"/> riceve prodotti letti e totale.</summary>
    public async Task<BudgetPlanData> CollectAsync(
        BudgetPlanRequest request, IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        var minRoi = Setting("MinOpenRoiPercent", 10m);
        var maxRoi = Setting("MaxOpenRoiPercent", 300m);
        var maxCandidates = (int)Setting("MaxCandidates", DefaultMaxCandidates);
        var maxHoldPremium = Setting("MaxHoldPremiumPercent", 10m) / 100m;
        var openShare = Math.Clamp(request.OpenSharePercent, 0, 100) / 100m;

        var rows = await LoadOpportunityRowsAsync(cancellationToken);

        var open = rows
            .Where(r => r.Decision == "Apri" && r.OpenValueCm > 0 && r.RoiPercent >= minRoi && r.RoiPercent <= maxRoi)
            .OrderByDescending(r => r.RoiPercent)
            .ToList();
        var hold = rows
            .Where(r => r.IsHoldCandidate && r.HoldScore != null)
            .OrderByDescending(r => r.HoldScore)
            .ToList();

        // Il limite dei prodotti da leggere su Card Trader si divide come il budget; i posti che una parte non
        // usa vanno all'altra, come il budget che le avanza.
        var openSlots = Math.Min(open.Count, (int)Math.Round(maxCandidates * openShare));
        var holdSlots = Math.Min(hold.Count, maxCandidates - openSlots);
        openSlots = Math.Min(open.Count, maxCandidates - holdSlots);

        var chosen = open.Take(openSlots).Select(r => (Row: r, Kind: BudgetKind.Open))
            .Concat(hold.Take(holdSlots).Select(r => (Row: r, Kind: BudgetKind.Hold)))
            .ToList();

        // Offerte dei venditori Cardmarket incollate (Fase 1): il prodotto entra anche se non è fra i migliori della
        // classifica, purché al prezzo del venditore convenga aprirlo o sia un candidato da tenere.
        var rowById = rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.First());
        var chosenKind = chosen.ToDictionary(c => c.Row.ProductId, c => c.Kind);
        var pasted = (await _sellerOffers.GetValidOffersAsync(cancellationToken))
            .Where(o => rowById.ContainsKey(o.SealedProductId))
            .Select(o =>
            {
                var row = rowById[o.SealedProductId];
                var kind = chosenKind.TryGetValue(row.ProductId, out var k) ? k : PastedKind(row, o.Price, minRoi, maxRoi);
                return (Offer: o, Row: row, Kind: kind);
            })
            .Where(x => x.Kind != null)
            .ToList();
        var allRows = chosen.Select(c => c.Row).Concat(pasted.Select(p => p.Row)).DistinctBy(r => r.ProductId).ToList();

        var copyLimits = await CopyLimitsAsync(allRows.Select(r => r.SetCode).Distinct().ToList(), cancellationToken);
        var held = await _db.ProductPurchases.AsNoTracking()
            .Where(p => p.HoldForResale && p.OpenedAt == null)
            .GroupBy(p => p.SealedProductId)
            .Select(g => new { g.Key, Quantity = g.Sum(p => p.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);
        var trendChanges = await TrendChangesAsync(allRows.Select(r => r.CardmarketId).OfType<int>().ToList(), cancellationToken);
        var cardmarketNames = await CardmarketNamesAsync(allRows.Select(r => r.CardmarketId).OfType<int>().ToList(), cancellationToken);

        var offers = new List<BudgetOffer>();
        var infos = new Dictionary<string, BudgetOfferInfo>();
        var withCardTrader = chosen.Count(c => c.Row.CardTraderBlueprintId != null);
        var done = 0;
        progress?.Report((0, withCardTrader));

        // Quello che vale per tutte le offerte di un prodotto: copie, valore, tetto dei tenuti, avvisi, nome Cardmarket.
        ProductTerms Terms(OpportunityRow row, BudgetKind kind)
        {
            var copies = copyLimits.GetValueOrDefault(row.SetCode, 2);
            if (kind == BudgetKind.Hold) copies = Math.Max(0, copies - held.GetValueOrDefault(row.ProductId));
            var value = kind == BudgetKind.Hold ? row.HoldScore!.Value : row.OpenValueCm!.Value;
            // Da tenere si compra al valore di mercato, non molto sopra: il tetto comprende la spedizione da pagare.
            decimal? cap = kind == BudgetKind.Hold && row.CmTrend is > 0 ? Math.Round(row.CmTrend.Value * (1 + maxHoldPremium), 2) : null;
            var flags = Flags(row, trendChanges.GetValueOrDefault(row.CardmarketId ?? 0));
            var cmName = row.CardmarketId is { } cmId ? cardmarketNames.GetValueOrDefault(cmId) : null;
            return new ProductTerms($"p:{row.ProductId}", copies, value, cap, flags, cmName);
        }

        BudgetOfferInfo InfoOf(OpportunityRow row, BudgetKind kind, ProductTerms t, string store, string? seller, string? country) => new(
            row.ProductId, row.Name, row.SetCode, kind, store, seller, country, row.OpenValueCm, row.RoiPercent,
            row.HoldScore, row.FloorPercent, t.Flags, row.CardTraderBlueprintId, t.CardmarketName, null, null, null);

        foreach (var (row, kind) in chosen)
        {
            var terms = Terms(row, kind);
            if (terms.MaxCopies == 0) continue;
            var (productKey, maxCopies, unitValue, maxCost) = (terms.ProductKey, terms.MaxCopies, terms.UnitValue, terms.MaxCost);
            BudgetOfferInfo Info(string store, string? seller, string? country) => InfoOf(row, kind, terms, store, seller, country);

            if (row.CardTraderBlueprintId is { } blueprintId)
            {
                var marketplace = await _offerCache.GetOrFetchAsync(blueprintId, OfferMaxAge,
                    ct => _cardTrader.GetMarketplaceProductsAsync(blueprintId, ct), cancellationToken);
                var valid = marketplace
                    .Where(o => o.BlueprintId == blueprintId && o.PriceCents > 0 && o.Quantity > 0 && !o.OnVacation)
                    .Where(o => IsEnglish(o.Properties.Language))
                    .OrderBy(o => o.PriceCents)
                    .Take(OffersPerProduct);
                foreach (var o in valid)
                {
                    var zero = o.User.CanSellSealedWithCtZero;
                    var key = $"ct:{o.Id}";
                    offers.Add(new BudgetOffer(key, productKey, zero ? "ctzero" : $"ct:{o.User.Id}", kind,
                        o.PriceCents / 100m, unitValue, o.Quantity, maxCopies, maxCost));
                    infos[key] = Info(zero ? "Card Trader Zero" : "Card Trader", o.User.Username, o.User.CountryCode) with { CardTraderProductId = o.Id };
                }
                progress?.Report((++done, withCardTrader));
            }

            // Cardmarket senza venditore (lo si cerca sul sito): al trend, o al prezzo più basso se è più alto, come
            // la classifica. Il solo prezzo più basso è l'offerta più economica in qualsiasi lingua, spesso non
            // inglese: confrontato con le offerte Card Trader in inglese vinceva sempre (prova del 17/10, Bloomburrow
            // Play Booster Box a 80 € contro 172 € di trend). Di quell'offerta si presume una sola copia.
            if (CardmarketBuyPrice(row) is { } cmPrice && row.CardmarketId != null)
            {
                var key = $"cm:{row.ProductId}";
                offers.Add(new BudgetOffer(key, productKey, key, kind, cmPrice, unitValue, 1, maxCopies, maxCost));
                infos[key] = Info("Cardmarket", null, null);
            }
        }

        foreach (var (offer, row, kind) in pasted)
        {
            var terms = Terms(row, kind!.Value);
            if (terms.MaxCopies == 0) continue;
            var key = $"cms:{offer.OfferId}";
            offers.Add(new BudgetOffer(key, terms.ProductKey, $"cms:{offer.Seller.ToLowerInvariant()}", kind.Value,
                offer.Price, terms.UnitValue, offer.Quantity, terms.MaxCopies, terms.MaxCost));
            var info = InfoOf(row, kind.Value, terms, "Cardmarket", offer.Seller, null);
            infos[key] = info with { Flags = info.Flags.Append($"Offerta incollata il {offer.PastedAt.ToLocalTime():dd/MM HH:mm}").ToList() };
        }

        await AddSecretLairAsync(request.IncludeSecretLairBorderline, offers, infos, cancellationToken);

        _logger.LogInformation(
            "Pianificatore del budget: {Open} candidati da aprire e {Hold} da tenere (su {OpenAll} e {HoldAll}), {Offers} offerte",
            openSlots, holdSlots, open.Count, hold.Count, offers.Count);

        return new BudgetPlanData(DateTime.UtcNow, request, offers, infos, open.Count, hold.Count, openSlots, holdSlots,
            Setting("MaxHoldProductSharePercent", 40m));
    }

    /// <summary>Righe della classifica di oggi, con i dati del prodotto che servono ai piani.</summary>
    public async Task<List<OpportunityRow>> LoadOpportunityRowsAsync(CancellationToken cancellationToken)
    {
        var lastDate = await _db.SealedOpportunities.Select(o => (DateOnly?)o.Date).MaxAsync(cancellationToken);
        var rows = lastDate == null
            ? new List<OpportunityRow>()
            : await _db.SealedOpportunities.AsNoTracking()
                .Where(o => o.Date == lastDate)
                .Select(o => new OpportunityRow(
                    o.SealedProductId, o.SealedProduct!.Name, o.MainSetCode, o.SealedProduct.CardTraderBlueprintId,
                    o.SealedProduct.CardmarketId, o.CmTrend, o.CmLow, o.OpenValueCm, o.OpeningRoiPercent, o.Decision,
                    o.HoldScore, o.FloorPercent, o.ReleaseAgeMonths, o.HistoryPosition, o.IsHoldCandidate))
                .ToListAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// Drop Secret Lair acquistabili su Wizards (disponibili o in preordine, vendita non chiusa) con verdetto "Compra",
    /// e "Al limite" se richiesto: valore netto stimato, margine e avvisi sul tempo.
    /// </summary>
    public async Task<List<WizardsDrop>> GetBuyableSecretLairAsync(bool includeBorderline, CancellationToken cancellationToken)
    {
        var shop = await _secretLairShop.GetAsync(cancellationToken);
        var buyable = shop.Products.Where(p => p.Status is "Disponibile" or "Preordine").ToDictionary(p => p.WizardsProductId);
        var drops = new List<WizardsDrop>();
        if (buyable.Count == 0) return drops;

        var valuation = await _secretLairValuation.GetAsync(cancellationToken);
        foreach (var estimate in valuation.Products)
        {
            if (!buyable.TryGetValue(estimate.WizardsProductId, out var product)) continue;
            if (estimate.Verdict != "Compra" && !(includeBorderline && estimate.Verdict == "Al limite")) continue;
            var value = estimate.RealNetValue ?? estimate.EstimatedNetValue;
            if (value is not > 0) continue;

            var flags = new List<string>();
            if (product.SaleEnd is { } end && end > DateTime.Now)
                flags.Add($"In vendita su Wizards ancora {(int)Math.Ceiling((end - DateTime.Now).TotalDays)} giorni");
            if (product.Status == "Preordine") flags.Add("Preordine");
            drops.Add(new WizardsDrop(product.WizardsProductId, product.Title, product.Price, value.Value, estimate.MarginPercent,
                estimate.Verdict, product.Url, flags));
        }
        return drops;
    }

    /// <summary>Un prodotto che c'è solo fra le offerte incollate: da aprire se conviene al prezzo del venditore, da tenere se è candidato.</summary>
    public static BudgetKind? PastedKind(OpportunityRow row, decimal price, decimal minRoi, decimal maxRoi)
    {
        if (SellerBasketBuilder.IsOpenable(row.Decision) && row.OpenValueCm is > 0 && price > 0)
        {
            var roi = (row.OpenValueCm.Value - price) / price * 100;
            if (roi >= minRoi && roi <= maxRoi) return BudgetKind.Open;
        }
        return row.IsHoldCandidate && row.HoldScore != null ? BudgetKind.Hold : null;
    }

    /// <summary>Prezzo d'acquisto su Cardmarket senza venditore: trend, o prezzo più basso se è più alto.</summary>
    public static decimal? CardmarketBuyPrice(OpportunityRow row)
    {
        var price = Math.Max(row.CmTrend ?? 0, row.CmLow ?? 0);
        return price > 0 ? price : null;
    }

    /// <summary>Negozio di un'offerta, per le caselle Card Trader / Cardmarket / Wizards della scheda.</summary>
    public static string StoreGroupOf(string offerKey) =>
        offerKey.StartsWith("ct:") ? "CardTrader" : offerKey.StartsWith("cm") ? "Cardmarket" : "Wizards";

    /// <summary>Il piano sulle offerte già lette: in millisecondi, senza Card Trader.</summary>
    /// <param name="excludedStores">Negozi su cui oggi non si compra ("CardTrader", "Cardmarket", "Wizards").</param>
    public static BudgetPlanResult Compose(
        BudgetPlanData data, BudgetShipping shipping, IReadOnlyCollection<string> excluded, IReadOnlyCollection<string>? excludedStores = null)
    {
        var stores = excludedStores?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
        var allExcluded = excluded.Concat(data.Offers.Where(o => stores.Contains(StoreGroupOf(o.OfferKey))).Select(o => o.OfferKey)).ToList();
        var maxHoldProductShare = data.MaxHoldProductSharePercent / 100m;
        var shippingByKey = data.Offers.Select(o => o.ShippingKey).Distinct().ToDictionary(k => k, k =>
            k == "ctzero" ? shipping.CardTraderZero
            : k.StartsWith("ct:") ? shipping.CardTrader
            : k.StartsWith("cm:") ? shipping.Cardmarket
            : shipping.Wizards);

        var request = data.Request;
        var choice = BudgetPlanner.Choose(data.Offers, request.Budget, Math.Clamp(request.OpenSharePercent, 0, 100) / 100m,
            shippingByKey, allExcluded, maxHoldProductShare);

        var baskets = choice.Lines
            .GroupBy(l => l.Offer.ShippingKey)
            .Select(g =>
            {
                var first = data.Infos[g.First().Offer.OfferKey];
                var lines = g.Select(l =>
                {
                    var info = data.Infos[l.Offer.OfferKey];
                    var gain = l.Offer.Kind == BudgetKind.Hold ? (decimal?)null : (l.Offer.UnitValue - l.Offer.UnitPrice) * l.Quantity;
                    return new BudgetPlanLine(l.Offer.OfferKey, info, l.Quantity, l.Offer.UnitPrice, gain);
                })
                .OrderBy(l => l.Info.Kind).ThenByDescending(l => l.Gain ?? 0).ToList();
                var ship = choice.ShippingPaid.GetValueOrDefault(g.Key);
                var total = lines.Sum(l => l.UnitPrice * l.Quantity) + ship;
                var basketGain = lines.Sum(l => l.Gain ?? 0) - (lines.Any(l => l.Gain != null) ? ship : 0);
                return new BudgetPlanBasket(g.Key, first.Store,
                    g.Key == "ctzero" || first.Store == "Wizards" ? null : first.Seller,
                    g.Key == "ctzero" ? null : first.Country, Math.Round(ship, 2), Math.Round(total, 2), Math.Round(basketGain, 2), lines);
            })
            .OrderBy(b => StoreOrder(b.Store)).ThenByDescending(b => b.Total)
            .ToList();

        var spent = Math.Round(choice.Spent, 2);
        var openGain = baskets.SelectMany(b => b.Lines).Sum(l => l.Gain ?? 0) - baskets.Where(b => b.Lines.Any(l => l.Gain != null)).Sum(b => b.Shipping);
        var holdFloor = baskets.SelectMany(b => b.Lines).Where(l => l.Info.Kind == BudgetKind.Hold)
            .Sum(l => (l.Info.OpenValueNet ?? 0) * l.Quantity);
        var holdSpent = baskets.SelectMany(b => b.Lines).Where(l => l.Info.Kind == BudgetKind.Hold).Sum(l => l.UnitPrice * l.Quantity);

        return new BudgetPlanResult(
            data.CollectedAt, request.Budget, request.OpenSharePercent, spent,
            Math.Round(choice.ShippingPaid.Values.Sum(), 2), Math.Round(openGain, 2), Math.Round(holdSpent, 2), Math.Round(holdFloor, 2),
            Math.Round(request.Budget - spent, 2), RemainingReason(data, choice, request.Budget - spent),
            shipping, data.OpenCandidates, data.HoldCandidates, data.OpenRead, data.HoldRead, excluded.ToList(), stores.ToList(), baskets);
    }

    private static int StoreOrder(string store) => store switch
    {
        "Card Trader Zero" => 0, "Card Trader" => 1, "Cardmarket" => 2, _ => 3
    };

    private static string? RemainingReason(BudgetPlanData data, BudgetChoice choice, decimal remaining)
    {
        if (remaining < 1) return null;
        if (data.Offers.Count == 0) return "Nessun candidato oggi: classifica vuota o nessun prodotto conveniente";
        var bought = choice.Lines.ToDictionary(l => l.Offer.OfferKey, l => l.Quantity);
        var cheapestLeft = data.Offers
            .Where(o => bought.GetValueOrDefault(o.OfferKey) < o.Available)
            .Select(o => o.UnitPrice).DefaultIfEmpty().Min();
        return cheapestLeft > remaining
            ? $"Nessun altro prodotto conveniente sotto i {remaining:0} €"
            : "Gli altri prodotti non convengono più (limite di copie o guadagno che non copre la spedizione)";
    }

    /// <summary>
    /// Copie al massimo per uscita: 3 se si vende almeno metà di quello che si carica, 2 fra un quarto e
    /// metà, 1 sotto. Uscite mai caricate, o con troppe poche copie per misurare: 2 (le proprie inserzioni
    /// non si fanno concorrenza, ma l'uscita è comunque un'incognita).
    /// </summary>
    private async Task<Dictionary<string, int>> CopyLimitsAsync(List<string> setCodes, CancellationToken cancellationToken)
    {
        var max = (int)Setting("MaxCopies", 3m);
        var codes = setCodes.Select(c => c.ToLowerInvariant()).ToList();

        var sold = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Blueprint != null && oi.Order.PaidAt != null && codes.Contains(oi.Blueprint.Expansion.Code.ToLower()))
            .GroupBy(oi => oi.Blueprint!.Expansion.Code.ToLower())
            .Select(g => new { g.Key, Quantity = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);
        var stock = await _db.InventoryItems.AsNoTracking()
            .Where(i => codes.Contains(i.Blueprint.Expansion.Code.ToLower()))
            .GroupBy(i => i.Blueprint.Expansion.Code.ToLower())
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, cancellationToken);

        return setCodes.ToDictionary(c => c, c =>
        {
            var key = c.ToLowerInvariant();
            return CopyLimit(sold.GetValueOrDefault(key), stock.GetValueOrDefault(key), max);
        });
    }

    public static int CopyLimit(int sold, int inStock, int max)
    {
        var total = sold + inStock;
        if (total < MinSellThroughCopies) return Math.Min(2, max);
        var share = (decimal)sold / total;
        return share >= 0.5m ? max : share >= 0.25m ? Math.Min(2, max) : 1;
    }

    /// <summary>Variazione del trend Cardmarket negli ultimi 7 giorni, in percentuale.</summary>
    private async Task<Dictionary<int, decimal>> TrendChangesAsync(List<int> cardmarketIds, CancellationToken cancellationToken)
    {
        if (cardmarketIds.Count == 0) return new();
        var since = DateOnly.FromDateTime(DateTime.Today.AddDays(-10));
        var snapshots = await _db.CardmarketPriceSnapshots.AsNoTracking()
            .Where(s => cardmarketIds.Contains(s.IdProduct) && s.Date >= since && s.Trend != null)
            .Select(s => new { s.IdProduct, s.Date, s.Trend })
            .ToListAsync(cancellationToken);

        return snapshots.GroupBy(s => s.IdProduct).Select(g =>
        {
            var last = g.MaxBy(s => s.Date)!;
            var before = g.Where(s => s.Date <= last.Date.AddDays(-7)).MaxBy(s => s.Date);
            return before is { Trend: > 0 }
                ? (Id: g.Key, Change: (decimal?)Math.Round((last.Trend!.Value - before.Trend.Value) / before.Trend.Value * 100, 1))
                : (Id: g.Key, Change: null);
        })
        .Where(x => x.Change != null)
        .ToDictionary(x => x.Id, x => x.Change!.Value);
    }

    private async Task<Dictionary<int, string>> CardmarketNamesAsync(List<int> cardmarketIds, CancellationToken cancellationToken) =>
        cardmarketIds.Count == 0
            ? new()
            : await _db.CardmarketProducts.AsNoTracking()
                .Where(p => cardmarketIds.Contains(p.IdProduct))
                .ToDictionaryAsync(p => p.IdProduct, p => p.Name, cancellationToken);

    /// <summary>Avvisi sul tempo: non cambiano la scelta, aiutano a decidere se comprare oggi.</summary>
    public static List<string> Flags(OpportunityRow row, decimal? trendChange7Days)
    {
        var flags = new List<string>();
        if (trendChange7Days is < -5) flags.Add($"In calo: {trendChange7Days:0.#}% in 7 giorni, forse conviene aspettare");
        if (row.HistoryPosition is < 30) flags.Add("Caro rispetto al suo storico");
        if (row.ReleaseAgeMonths is < 6) flags.Add("Uscita recente: di solito il prezzo scende ancora");
        return flags;
    }

    /// <summary>Secret Lair in vendita su Wizards con verdetto "Compra" (e "Al limite" se richiesto): una copia, si vende carta per carta.</summary>
    private async Task AddSecretLairAsync(
        bool includeBorderline, List<BudgetOffer> offers, Dictionary<string, BudgetOfferInfo> infos, CancellationToken cancellationToken)
    {
        foreach (var drop in await GetBuyableSecretLairAsync(includeBorderline, cancellationToken))
        {
            var key = $"wz:{drop.WizardsProductId}";
            offers.Add(new BudgetOffer(key, key, "wizards", BudgetKind.SecretLair, drop.Price, drop.NetValue, 1, 1));
            infos[key] = new BudgetOfferInfo(null, drop.Title, null, BudgetKind.SecretLair, "Wizards", null, null,
                drop.NetValue, drop.MarginPercent, null, null, drop.Flags, null, null, drop.Url, null, drop.Verdict);
        }
    }

    /// <summary>Si compra solo in inglese; un'offerta senza lingua indicata è accettata (sigillati).</summary>
    private static bool IsEnglish(string? language) =>
        string.IsNullOrEmpty(language)
        || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "English", StringComparison.OrdinalIgnoreCase);
}

/// <param name="OpenSharePercent">Parte del budget per i prodotti da aprire (0–100); il resto per quelli da tenere.</param>
/// <param name="IncludeSecretLairBorderline">Anche i Secret Lair "Al limite", oltre ai "Compra".</param>
/// <param name="Shipping">Spedizioni da usare al posto delle predefinite.</param>
/// <param name="ExcludedStores">Negozi su cui oggi non si compra ("CardTrader", "Cardmarket", "Wizards").</param>
public record BudgetPlanRequest(
    decimal Budget, int OpenSharePercent, bool IncludeSecretLairBorderline, BudgetShipping? Shipping, List<string>? ExcludedStores = null);

public record BudgetShipping(decimal CardTrader, decimal CardTraderZero, decimal Cardmarket, decimal Wizards);

public record OpportunityRow(
    int ProductId, string Name, string SetCode, int? CardTraderBlueprintId, int? CardmarketId, decimal? CmTrend, decimal? CmLow,
    decimal? OpenValueCm, decimal? RoiPercent, string? Decision, decimal? HoldScore, decimal? FloorPercent,
    int? ReleaseAgeMonths, decimal? HistoryPosition, bool IsHoldCandidate);

/// <summary>Quello che serve a mostrare un'offerta: prodotto, negozio, valori e link.</summary>
/// <param name="OpenValueNet">Valore atteso netto dell'apertura (per i tenuti è il pavimento in euro).</param>
/// <param name="RoiPercent">Resa dell'apertura nella classifica; per i Secret Lair il margine stimato.</param>
/// <param name="Verdict">Solo Secret Lair: "Compra" o "Al limite".</param>
public record BudgetOfferInfo(
    int? ProductId, string Name, string? SetCode, BudgetKind Kind, string Store, string? Seller, string? Country,
    decimal? OpenValueNet, decimal? RoiPercent, decimal? HoldScore, decimal? FloorPercent, List<string> Flags,
    int? CardTraderBlueprintId, string? CardmarketName, string? WizardsUrl, int? CardTraderProductId, string? Verdict);

/// <summary>Offerte lette e candidati, conservati per ricalcolare il piano senza rileggere Card Trader.</summary>
public record BudgetPlanData(
    DateTime CollectedAt, BudgetPlanRequest Request, List<BudgetOffer> Offers, Dictionary<string, BudgetOfferInfo> Infos,
    int OpenCandidates, int HoldCandidates, int OpenRead, int HoldRead, decimal MaxHoldProductSharePercent);

/// <param name="Gain">Guadagno atteso (valore netto − prezzo) per la quantità; null per i tenuti.</param>
public record BudgetPlanLine(string OfferKey, BudgetOfferInfo Info, int Quantity, decimal UnitPrice, decimal? Gain);

/// <param name="Seller">Venditore Card Trader; null per CT Zero (più venditori, una spedizione), Cardmarket e Wizards.</param>
/// <param name="Gain">Guadagno atteso delle aperture del carrello, spedizione compresa.</param>
public record BudgetPlanBasket(
    string Key, string Store, string? Seller, string? Country, decimal Shipping, decimal Total, decimal Gain, List<BudgetPlanLine> Lines);

/// <param name="OpenGain">Guadagno atteso delle aperture e dei Secret Lair, spedizioni comprese.</param>
/// <param name="HoldFloor">Valore delle carte dei tenuti (il caso peggiore: aprirli).</param>
public record BudgetPlanResult(
    DateTime CollectedAt, decimal Budget, int OpenSharePercent, decimal Spent, decimal Shipping, decimal OpenGain,
    decimal HoldSpent, decimal HoldFloor, decimal Remaining, string? RemainingReason, BudgetShipping ShippingUsed,
    int OpenCandidates, int HoldCandidates, int OpenRead, int HoldRead, List<string> Excluded, List<string> ExcludedStores,
    List<BudgetPlanBasket> Baskets);

/// <summary>Termini comuni a tutte le offerte di un prodotto nel piano.</summary>
public record ProductTerms(string ProductKey, int MaxCopies, decimal UnitValue, decimal? MaxCost, List<string> Flags, string? CardmarketName);

/// <summary>Drop Secret Lair acquistabile su Wizards, con la sua stima.</summary>
public record WizardsDrop(string WizardsProductId, string Title, decimal Price, decimal NetValue, decimal? MarginPercent,
    string Verdict, string Url, List<string> Flags);
