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
                    if (o.PriceCents <= 0 || o.Quantity <= 0 || o.OnVacation || !IsEnglish(o.Properties.Language)) continue;
                    offers.Add(new SellerOffer(o.User.CanSellSealedWithCtZero ? "Card Trader Zero" : "Card Trader", o.User.Username,
                        o.User.CountryCode, productId, o.PriceCents / 100m, o.Quantity, o.User.CanSellSealedWithCtZero, o.Id, null));
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
                var holdOk = p.IsHoldCandidate && p.CmTrend is > 0 && offer.Price <= p.CmTrend.Value * (1 + rules.MaxHoldPremiumPercent / 100);
                var (group, verdict) =
                    inBasket.TryGetValue(offer.ProductId, out var kind) ? (0, kind == SellerLineKind.Open ? "Nel carrello: da aprire" : "Nel carrello: da tenere")
                    : roi > MaxRealisticRoi ? (3, "Prezzo da controllare: resa troppo alta per essere vera")
                    : roi >= rules.AddOnRoiPercent && SellerBasketBuilder.IsNotPaidBack(p)
                        ? (2, string.Format(Italian, "Da aprire, ma già comprato e non ancora ripagato (venduto {0:0}%)", p.History!.SoldPercent ?? 0))
                    : roi >= rules.AddOnRoiPercent ? (1, basket == null
                        ? "Da aprire, ma senza un traino questo venditore non fa un carrello"
                        : "Da aprire, fuori dal carrello: non ci sta nel budget o abbassa il netto sotto la soglia")
                    : holdOk ? (1, basket == null
                        ? "Da tenere, ma senza un'apertura questo venditore non fa un carrello"
                        : "Da tenere, fuori dal carrello: non ci sta nel budget")
                    : p.IsHoldCandidate && p.CmTrend is > 0
                        ? (2, string.Format(Italian, "Da tenere, ma caro: {0:0}% sopra il trend", (offer.Price / p.CmTrend.Value - 1) * 100))
                    : roi > 0 ? (2, string.Format(Italian, "Rende il {0:0}%, sotto la soglia \"altri\" ({1:0}%)", roi, rules.AddOnRoiPercent))
                    : roi != null ? (3, "Aprirlo non rende a questo prezzo")
                    : (3, p.Decision is { } d ? $"Nessuna resa affidabile: {d}" : "Nessuna valutazione oggi");
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
