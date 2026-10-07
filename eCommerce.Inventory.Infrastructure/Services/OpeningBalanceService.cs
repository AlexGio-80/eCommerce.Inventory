using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Bilancio reale di ogni apertura, ricostruito dal tag delle inserzioni: il tag è lo stesso sulle
/// carte caricate (<c>PendingListings</c>, con il costo ripartito per carta), su quelle vendute
/// (<c>OrderItems</c>) e su quelle ancora in vendita (<c>InventoryItems</c>).
///
/// Le vendite contano solo da un giorno prima del primo caricamento: il recupero dei tag sugli ordini
/// storici ne ha attribuiti alcuni a ordini di anni prima, per carte con lo stesso blueprint.
/// </summary>
public class OpeningBalanceService
{
    private static readonly int[] CurveDays = { 30, 60, 90, 180 };

    /// <summary>Sotto queste copie un tag non è un'apertura (correzioni, singole carte).</summary>
    private const int MinCopies = 20;

    /// <summary>
    /// Sotto questo costo un tag non è un'apertura di box ma un lotto di carte di vecchie collezioni
    /// (i vari "_OLD" da pochi euro): affollerebbero il bilancio senza dire niente.
    /// </summary>
    private const decimal MinCost = 50m;

    private readonly ApplicationDbContext _db;

    public OpeningBalanceService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<OpeningBalance>> GetAsync(decimal bulkThreshold, CancellationToken cancellationToken = default)
    {
        var uploads = await _db.PendingListings.AsNoTracking()
            .Where(pl => pl.Tag != null && pl.Tag != "")
            .Select(pl => new UploadRow(pl.Id, pl.Tag!, pl.Quantity, pl.PurchasePrice, pl.CreatedAt, pl.IsUpdate,
                pl.CardTraderProductId, pl.Blueprint.Expansion.Name, pl.Blueprint.Expansion.ReleaseDate))
            .ToListAsync(cancellationToken);

        var addedCopies = await AddedCopiesAsync(uploads, cancellationToken);

        var openings = uploads
            .GroupBy(u => NormalizeTag(u.Tag))
            .Select(g => new
            {
                Key = g.Key,
                Tag = g.First().Tag,
                FirstUpload = g.Min(u => u.CreatedAt),
                Copies = g.Sum(u => addedCopies[u.Id]),
                Cost = g.Sum(u => u.PurchasePrice * addedCopies[u.Id]),
                Expansion = g.GroupBy(u => u.ExpansionName).OrderByDescending(e => e.Count()).First().Key,
                ReleaseDate = g.GroupBy(u => u.ExpansionName).OrderByDescending(e => e.Count()).First().First().ReleaseDate
            })
            .Where(o => o.Copies >= MinCopies && o.Cost >= MinCost)
            .ToList();

        // Confronto sul tag normalizzato anche nel database: lo stesso tag può essere scritto con o
        // senza "#" o con maiuscole diverse fra caricamento, vendita e registro acquisti.
        var keys = openings.Select(o => o.Key).ToList();

        var sales = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Tag != null && keys.Contains(oi.Tag.Trim().Replace("#", "").ToUpper()) && oi.Order.PaidAt != null)
            .Select(oi => new { oi.Tag, oi.Quantity, oi.Price, PaidAt = oi.Order.PaidAt!.Value })
            .ToListAsync(cancellationToken);

        var stock = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.Tag != null && keys.Contains(i.Tag.Trim().Replace("#", "").ToUpper()))
            .Select(i => new { i.Tag, i.Quantity, i.ListingPrice })
            .ToListAsync(cancellationToken);

        var purchases = await _db.ProductPurchases.AsNoTracking()
            .Where(p => p.Tag != null)
            .Select(p => new { p.Tag, p.Quantity, p.UnitPrice, p.PredictedOpenValueNet })
            .ToListAsync(cancellationToken);

        var fees = await _db.Orders.AsNoTracking()
            .Where(o => o.PaidAt != null)
            .GroupBy(o => 1)
            .Select(g => new { Fee = g.Sum(o => o.SellerFee), Subtotal = g.Sum(o => o.SellerSubtotal) })
            .FirstOrDefaultAsync(cancellationToken);
        var feeShare = fees is { Subtotal: > 0 } ? fees.Fee / fees.Subtotal : 0m;

        var today = DateTime.UtcNow;

        return openings.Select(o =>
            {
                var tagSales = sales.Where(s => NormalizeTag(s.Tag!) == o.Key && s.PaidAt >= o.FirstUpload.AddDays(-1)).ToList();
                var tagStock = stock.Where(s => NormalizeTag(s.Tag!) == o.Key).ToList();
                var tagPurchases = purchases.Where(p => NormalizeTag(p.Tag!) == o.Key).ToList();

                var gross = tagSales.Sum(s => s.Quantity * s.Price);
                var net = gross * (1 - feeShare);
                var ageDays = (int)(today - o.FirstUpload).TotalDays;

                var curve = CurveDays
                    .Where(days => ageDays >= days)
                    .Select(days => new OpeningCurvePoint(days,
                        Math.Round(tagSales.Where(s => s.PaidAt <= o.FirstUpload.AddDays(days)).Sum(s => s.Quantity * s.Price) * (1 - feeShare), 2)))
                    .ToList();

                decimal? predicted = tagPurchases.Count > 0 && tagPurchases.All(p => p.PredictedOpenValueNet.HasValue)
                    ? tagPurchases.Sum(p => p.Quantity * p.PredictedOpenValueNet!.Value)
                    : null;

                // Box aperti all'uscita, il caso dell'analisi acquisti; i lotti di vecchie collezioni
                // comprati a peso hanno tutt'altra resa e la pagina li può nascondere.
                var openedAtRelease = o.ReleaseDate is { } release
                                      && o.FirstUpload >= release.AddDays(-15) && o.FirstUpload <= release.AddDays(45);

                return new OpeningBalance(
                    o.Tag, o.Expansion, o.ReleaseDate, openedAtRelease, o.FirstUpload, ageDays,
                    o.Copies, Math.Round(o.Cost, 2),
                    tagSales.Sum(s => s.Quantity), Math.Round(gross, 2), Math.Round(net, 2),
                    tagStock.Sum(s => s.Quantity), Math.Round(tagStock.Sum(s => s.Quantity * s.ListingPrice), 2),
                    tagStock.Where(s => s.ListingPrice <= bulkThreshold).Sum(s => s.Quantity),
                    Math.Round(net - o.Cost, 2),
                    o.Cost > 0 ? Math.Round((net - o.Cost) / o.Cost * 100m, 1) : null,
                    curve,
                    tagPurchases.Count > 0 ? Math.Round(tagPurchases.Sum(p => p.Quantity * p.UnitPrice), 2) : null,
                    predicted.HasValue ? Math.Round(predicted.Value, 2) : null);
            })
            .OrderByDescending(b => b.FirstUpload)
            .ToList();
    }

    /// <summary>
    /// Copie davvero aggiunte da ogni caricamento. Una modifica fatta dalla maschera (<c>IsUpdate</c>)
    /// porta la nuova quantità totale dell'inserzione, non le copie aggiunte: contarla per intero
    /// riconterebbe tutte le copie già presenti, con il loro costo. La quantità precedente si prende
    /// dallo storico prezzi (che registra anche la quantità) o, prima che esistesse, dal caricamento
    /// precedente della stessa inserzione. Con vendite avvenute nel frattempo la stima è leggermente
    /// per difetto; senza nessun dato precedente la modifica si conta per intero.
    /// </summary>
    private async Task<Dictionary<int, int>> AddedCopiesAsync(List<UploadRow> uploads, CancellationToken cancellationToken)
    {
        var result = uploads.ToDictionary(u => u.Id, u => u.Quantity);
        var updates = uploads.Where(u => u.IsUpdate && u.CardTraderProductId.HasValue).ToList();
        if (updates.Count == 0) return result;

        var productIds = updates.Select(u => u.CardTraderProductId!.Value).Distinct().ToList();

        var history = (await _db.PriceHistoryEntries.AsNoTracking()
                .Where(h => productIds.Contains(h.CardTraderProductId))
                .Select(h => new { h.CardTraderProductId, h.RecordedAt, h.Quantity })
                .ToListAsync(cancellationToken))
            .GroupBy(h => h.CardTraderProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.RecordedAt).ToList());

        var previousUploads = (await _db.PendingListings.AsNoTracking()
                .Where(pl => pl.CardTraderProductId != null && productIds.Contains(pl.CardTraderProductId.Value))
                .Select(pl => new { pl.Id, ProductId = pl.CardTraderProductId!.Value, pl.CreatedAt, pl.Quantity })
                .ToListAsync(cancellationToken))
            .GroupBy(pl => pl.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(pl => pl.CreatedAt).ToList());

        foreach (var update in updates)
        {
            var productId = update.CardTraderProductId!.Value;
            int? before = history.TryGetValue(productId, out var points)
                ? points.LastOrDefault(h => h.RecordedAt < update.CreatedAt)?.Quantity
                : null;
            before ??= previousUploads.TryGetValue(productId, out var rows)
                ? rows.LastOrDefault(r => r.CreatedAt < update.CreatedAt && r.Id != update.Id)?.Quantity
                : null;

            result[update.Id] = before is { } previous ? Math.Max(0, update.Quantity - previous) : update.Quantity;
        }

        return result;
    }

    private record UploadRow(int Id, string Tag, int Quantity, decimal PurchasePrice, DateTime CreatedAt,
        bool IsUpdate, int? CardTraderProductId, string ExpansionName, DateTime? ReleaseDate);

    /// <summary>"#TRK_PB_20261115" e "trk_pb_20261115" sono lo stesso tag.</summary>
    public static string NormalizeTag(string tag) => tag.Trim().Replace("#", "").ToUpperInvariant();
}

/// <param name="NetRevenue">Incassato al netto della commissione Card Trader misurata sugli ordini.</param>
/// <param name="StockListingValue">Valore delle copie ancora in vendita ai prezzi di listino: non è
/// quanto se ne ricaverà, il bulk in particolare si vende solo in parte.</param>
/// <param name="ProfitSoFar">Incassato netto meno costo: le carte ancora in vendita non sono contate.</param>
/// <param name="RegisteredCost">Costo dal registro acquisti, se l'apertura vi è registrata.</param>
/// <param name="PredictedNet">Previsione del modello all'apertura, se registrata.</param>
public record OpeningBalance(
    string Tag,
    string Expansion,
    DateTime? ExpansionReleaseDate,
    bool OpenedAtRelease,
    DateTime FirstUpload,
    int AgeDays,
    int Copies,
    decimal Cost,
    int SoldCopies,
    decimal GrossRevenue,
    decimal NetRevenue,
    int StockCopies,
    decimal StockListingValue,
    int StockBulkCopies,
    decimal ProfitSoFar,
    decimal? ProfitSoFarPercent,
    List<OpeningCurvePoint> RevenueCurve,
    decimal? RegisteredCost,
    decimal? PredictedNet);

/// <summary>Incassato netto entro <see cref="Days"/> giorni dal primo caricamento.</summary>
public record OpeningCurvePoint(int Days, decimal NetRevenue);
