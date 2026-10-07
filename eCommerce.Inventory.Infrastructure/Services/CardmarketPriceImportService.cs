using System.Globalization;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.Cardmarket;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Import del listino prezzi pubblico di Cardmarket nello storico locale.
///
/// È la base dell'analisi acquisti: Cardmarket e Card Trader danno solo il prezzo di oggi, quindi
/// l'andamento di un box dal preordine in poi esiste solo se lo salviamo noi, un giorno alla volta.
///
/// Cosa si salva:
/// - tutti i sigillati, una riga al giorno (circa 5.000 prodotti);
/// - le singole delle espansioni comparse nel catalogo negli ultimi
///   <c>CardmarketImport:SinglesTrackingMonths</c> mesi, e solo quando un prezzo cambia.
///
/// Le espansioni recenti si riconoscono dalla data in cui Cardmarket ha aggiunto la prima carta,
/// non dalla data di uscita su <see cref="Expansion"/>: le espansioni "Collectors" di Card Trader
/// (es. "Star Trek Collectors") non hanno data di uscita, e si perderebbero proprio le carte
/// Collector.
///
/// Non chiama Card Trader, quindi non consuma il limite di 20 richieste al minuto e non va
/// coordinato con la notturna dell'autopricer.
/// </summary>
public class CardmarketPriceImportService
{
    /// <summary>
    /// Un solo import alla volta in tutto il processo: lo statico copre sia il worker sia il
    /// pulsante manuale, che usano istanze diverse del servizio.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly ICardmarketDownloadClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CardmarketPriceImportService> _logger;

    public CardmarketPriceImportService(
        ApplicationDbContext db,
        ICardmarketDownloadClient client,
        IConfiguration configuration,
        ILogger<CardmarketPriceImportService> logger)
    {
        _db = db;
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public static bool IsRunning => Gate.CurrentCount == 0;

    /// <summary>
    /// Esegue l'import e ne restituisce l'esito, che resta anche a registro. Gli errori non escono
    /// come eccezioni: finiscono nel registro con esito <see cref="CardmarketImportOutcome.Failed"/>,
    /// così il worker resta in piedi per il giorno dopo.
    /// </summary>
    /// <param name="force">Ignora il controllo su <c>Last-Modified</c>. Non reimporta comunque un
    /// listino di un giorno già presente: la serie ha una riga per giorno.</param>
    /// <exception cref="CardmarketImportAlreadyRunningException">Se un altro import è in corso.</exception>
    public async Task<CardmarketImportLog> ImportAsync(
        CardmarketImportTrigger trigger,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            throw new CardmarketImportAlreadyRunningException();
        }

        try
        {
            return await RunAsync(trigger, force, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<CardmarketImportLog> RunAsync(
        CardmarketImportTrigger trigger, bool force, CancellationToken cancellationToken)
    {
        var log = new CardmarketImportLog { Trigger = trigger, Outcome = CardmarketImportOutcome.Running };
        _db.CardmarketImportLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Import listino Cardmarket: avvio ({Trigger})", trigger);

        try
        {
            await ImportCoreAsync(log, force, cancellationToken);
        }
        catch (Exception ex)
        {
            // Le righe già aggiunte ma non salvate non devono finire nel salvataggio dell'esito.
            _db.ChangeTracker.Clear();
            _db.CardmarketImportLogs.Attach(log);

            log.Outcome = CardmarketImportOutcome.Failed;
            log.Message = Truncate(ex is OperationCanceledException
                ? "Import interrotto prima della fine: nessuna riga di prezzo salvata"
                : $"{ex.GetType().Name}: {ex.Message}");

            if (ex is OperationCanceledException)
                _logger.LogWarning("Import listino Cardmarket interrotto");
            else
                _logger.LogError(ex, "Import listino Cardmarket fallito");
        }

        log.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation(
            "Import listino Cardmarket: {Outcome}. Sigillati {SealedWritten}/{Sealed}, singole seguite {Singles} ({SinglesWritten} variazioni), prodotti nuovi {New}. {Message}",
            log.Outcome, log.SealedSnapshotsWritten, log.SealedProducts, log.SinglesTracked,
            log.SinglesSnapshotsWritten, log.NewProducts, log.Message);

        return log;
    }

    private async Task ImportCoreAsync(CardmarketImportLog log, bool force, CancellationToken cancellationToken)
    {
        log.SourceLastModified = await _client.GetPriceGuideLastModifiedAsync(cancellationToken);

        if (!force && log.SourceLastModified is not null)
        {
            var lastImported = await _db.CardmarketImportLogs
                .Where(l => l.Outcome == CardmarketImportOutcome.Succeeded)
                .OrderByDescending(l => l.StartedAt)
                .Select(l => l.SourceLastModified)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastImported == log.SourceLastModified)
            {
                log.Outcome = CardmarketImportOutcome.Skipped;
                log.Message = "Listino invariato dall'ultimo import riuscito";
                return;
            }
        }

        var priceGuide = await _client.GetPriceGuideAsync(cancellationToken);
        log.SourceCreatedAt = priceGuide.GetCreatedAt();

        // Il giorno è quello del listino nel suo fuso (Cardmarket lo genera di notte, ora italiana),
        // non quello dell'import: un import in ritardo non deve spostare il punto della serie.
        var snapshotDate = DateOnly.FromDateTime(log.SourceCreatedAt.Value.DateTime);

        if (await _db.CardmarketPriceSnapshots.AnyAsync(s => s.Date == snapshotDate, cancellationToken))
        {
            log.Outcome = CardmarketImportOutcome.Skipped;
            log.Message = $"Listino del {snapshotDate:dd/MM/yyyy} già importato";
            return;
        }

        var sealedCatalog = await _client.GetSealedProductsAsync(cancellationToken);
        var singlesCatalog = await _client.GetSingleProductsAsync(cancellationToken);

        var trackingMonths = _configuration.GetValue("CardmarketImport:SinglesTrackingMonths", 12);
        var trackedSingles = SelectTrackedSingles(singlesCatalog.Products, snapshotDate, trackingMonths);

        var prices = priceGuide.PriceGuides
            .GroupBy(p => p.IdProduct)
            .ToDictionary(g => g.Key, g => g.First());

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            log.NewProducts = await UpsertCatalogAsync(sealedCatalog.Products, trackedSingles, cancellationToken);

            log.SealedProducts = sealedCatalog.Products.Count;
            log.SealedSnapshotsWritten = AddSealedSnapshots(sealedCatalog.Products, prices, snapshotDate);

            log.SinglesTracked = trackedSingles.Count;
            log.SinglesSnapshotsWritten = await AddChangedSingleSnapshotsAsync(
                trackedSingles, prices, snapshotDate, cancellationToken);

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }

        log.Outcome = CardmarketImportOutcome.Succeeded;
        log.Message = $"Listino del {snapshotDate:dd/MM/yyyy}";
    }

    /// <summary>
    /// Singole delle espansioni la cui prima carta è comparsa nel catalogo entro la finestra.
    /// Si ragiona per espansione, non per carta: una ristampa aggiunta oggi a un'espansione vecchia
    /// non deve far seguire mezza espansione, e una carta svelata tardi in un'espansione nuova sì.
    /// </summary>
    public static List<CardmarketCatalogEntry> SelectTrackedSingles(
        IReadOnlyCollection<CardmarketCatalogEntry> singles, DateOnly referenceDate, int trackingMonths)
    {
        var cutoff = referenceDate.AddMonths(-trackingMonths).ToDateTime(TimeOnly.MinValue);

        var recentExpansions = singles
            .Select(p => new { p.IdExpansion, DateAdded = ParseDateAdded(p.DateAdded) })
            .Where(p => p.DateAdded is not null)
            .GroupBy(p => p.IdExpansion)
            .Where(g => g.Min(p => p.DateAdded) >= cutoff)
            .Select(g => g.Key)
            .ToHashSet();

        return singles.Where(p => recentExpansions.Contains(p.IdExpansion)).ToList();
    }

    private async Task<int> UpsertCatalogAsync(
        IReadOnlyCollection<CardmarketCatalogEntry> sealedProducts,
        IReadOnlyCollection<CardmarketCatalogEntry> trackedSingles,
        CancellationToken cancellationToken)
    {
        var existing = await _db.CardmarketProducts.ToDictionaryAsync(p => p.IdProduct, cancellationToken);
        var now = DateTime.UtcNow;
        var added = 0;

        var entries = sealedProducts.Select(p => (Entry: p, IsSingle: false))
            .Concat(trackedSingles.Select(p => (Entry: p, IsSingle: true)));

        foreach (var (entry, isSingle) in entries)
        {
            if (!existing.TryGetValue(entry.IdProduct, out var product))
            {
                product = new CardmarketProduct { IdProduct = entry.IdProduct, FirstSeenAt = now };
                _db.CardmarketProducts.Add(product);
                existing[entry.IdProduct] = product;
                added++;
            }

            // I nomi dei preordini cambiano (es. le virgolette dei titoli): si tiene l'ultimo.
            product.Name = entry.Name;
            product.IdCategory = entry.IdCategory;
            product.CategoryName = entry.CategoryName;
            product.IdExpansion = entry.IdExpansion;
            product.DateAdded = ParseDateAdded(entry.DateAdded);
            product.IsSingle = isSingle;
            product.LastSeenAt = now;
        }

        return added;
    }

    private int AddSealedSnapshots(
        IEnumerable<CardmarketCatalogEntry> sealedProducts,
        IReadOnlyDictionary<int, CardmarketPriceGuideEntry> prices,
        DateOnly snapshotDate)
    {
        var written = 0;
        foreach (var product in sealedProducts)
        {
            if (!prices.TryGetValue(product.IdProduct, out var price)) continue;

            // Foil sui sigillati non ha senso: Cardmarket li riporta vuoti o a zero.
            _db.CardmarketPriceSnapshots.Add(ToSnapshot(price, snapshotDate, includeFoil: false));
            written++;
        }

        return written;
    }

    private async Task<int> AddChangedSingleSnapshotsAsync(
        IReadOnlyCollection<CardmarketCatalogEntry> trackedSingles,
        IReadOnlyDictionary<int, CardmarketPriceGuideEntry> prices,
        DateOnly snapshotDate,
        CancellationToken cancellationToken)
    {
        var trackedIds = trackedSingles.Select(p => p.IdProduct).ToList();

        // Ultima riga salvata per ciascuna singola seguita: il confronto è con quella, non con il
        // listino di ieri, perché le singole si salvano solo quando cambiano.
        var latestDates = _db.CardmarketPriceSnapshots
            .Where(s => trackedIds.Contains(s.IdProduct))
            .GroupBy(s => s.IdProduct)
            .Select(g => new { IdProduct = g.Key, Date = g.Max(s => s.Date) });

        var latest = await (
                from s in _db.CardmarketPriceSnapshots
                join l in latestDates on new { s.IdProduct, s.Date } equals new { l.IdProduct, l.Date }
                select s)
            .AsNoTracking()
            .ToDictionaryAsync(s => s.IdProduct, cancellationToken);

        var written = 0;
        foreach (var id in trackedIds)
        {
            if (!prices.TryGetValue(id, out var price)) continue;

            var snapshot = ToSnapshot(price, snapshotDate, includeFoil: true);
            if (latest.TryGetValue(id, out var previous) && !HasChanged(previous, snapshot)) continue;

            _db.CardmarketPriceSnapshots.Add(snapshot);
            written++;
        }

        return written;
    }

    /// <summary>
    /// Avg1/Avg7/Avg30 sono esclusi dal confronto di proposito: cambiano quasi ogni giorno per
    /// costruzione e annullerebbero il risparmio della serie a variazione.
    /// </summary>
    public static bool HasChanged(CardmarketPriceSnapshot previous, CardmarketPriceSnapshot current) =>
        previous.Trend != current.Trend
        || previous.Low != current.Low
        || previous.Avg != current.Avg
        || previous.TrendFoil != current.TrendFoil
        || previous.LowFoil != current.LowFoil
        || previous.AvgFoil != current.AvgFoil;

    private static CardmarketPriceSnapshot ToSnapshot(CardmarketPriceGuideEntry price, DateOnly date, bool includeFoil) => new()
    {
        IdProduct = price.IdProduct,
        Date = date,
        Avg = price.Avg,
        Low = price.Low,
        Trend = price.Trend,
        Avg1 = price.Avg1,
        Avg7 = price.Avg7,
        Avg30 = price.Avg30,
        AvgFoil = includeFoil ? price.AvgFoil : null,
        LowFoil = includeFoil ? price.LowFoil : null,
        TrendFoil = includeFoil ? price.TrendFoil : null
    };

    private static DateTime? ParseDateAdded(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}

public class CardmarketImportAlreadyRunningException : InvalidOperationException
{
    public CardmarketImportAlreadyRunningException()
        : base("Un import del listino Cardmarket è già in corso")
    {
    }
}
