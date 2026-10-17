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
        var (expansionsRead, failures) = await AddCardTraderAsync(toRead, offers, progress, cancellationToken);

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

    private async Task<(int Read, int Failures)> AddCardTraderAsync(
        List<SellerProduct> products, List<SellerOffer> offers, IProgress<(int, int)>? progress, CancellationToken cancellationToken)
    {
        var productByBlueprint = products
            .Where(p => p.CardTraderBlueprintId != null)
            .GroupBy(p => p.CardTraderBlueprintId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ProductId);
        var blueprintIds = productByBlueprint.Keys.ToList();
        var expansionByBlueprint = await _db.Blueprints.AsNoTracking()
            .Where(b => blueprintIds.Contains(b.CardTraderId))
            .Select(b => new { b.CardTraderId, ExpansionCtId = b.Expansion.CardTraderId })
            .ToDictionaryAsync(b => b.CardTraderId, b => b.ExpansionCtId, cancellationToken);
        var expansions = expansionByBlueprint.Values.Distinct().ToList();

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
    public static SellerPlanResult Compose(SellerPlanData data, SellerPlanRequest request)
    {
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
            data.Offers.Where(o => o.Store == "Cardmarket").Select(o => o.Seller).Distinct().Count());
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

public record WizardsChoice(WizardsDrop Drop, bool Chosen);

/// <param name="InBudgetCount">Venditori che stanno nel budget, presi in ordine di guadagno.</param>
public record SellerPlanResult(
    DateTime CollectedAt, SellerPlanRequest Request, List<SellerBasket> Sellers, int InBudgetCount, decimal InBudgetTotal,
    decimal InBudgetGain, List<WizardsChoice> Wizards, decimal WizardsTotal, decimal WizardsGain, int Products,
    int ExpansionsRead, int ExpansionFailures, int CardmarketSellers);

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
            run.Result = SellerPlanService.Compose(data, request);
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
        using (var scope = _scopeFactory.CreateScope())
        {
            var sellerOffers = scope.ServiceProvider.GetRequiredService<CardmarketSellerOfferService>();
            run.Data = SellerPlanService.WithCardmarketOffers(run.Data, await sellerOffers.GetValidOffersAsync(cancellationToken));
        }
        run.Result = SellerPlanService.Compose(run.Data, request);
        return run.Status();
    }

    private async Task ExecuteAsync(Run run)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SellerPlanService>();
            var progress = new Progress<(int Done, int Total)>(p => { run.Done = p.Done; run.Total = p.Total; });
            run.Data = await service.CollectAsync(progress, CancellationToken.None);
            run.Result = SellerPlanService.Compose(run.Data, run.Request);
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
