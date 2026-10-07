using System.Text.Json;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Quanto si incassa davvero vendendo una carta, rispetto al trend Cardmarket: il valore atteso
/// usa il trend CM, ma si vende su Card Trader e a prezzi propri. Al 07/10/2026, sulle carte da 1 €
/// in su, il rapporto era 1,17 (1,29 fra 1 e 3 €, 1,16 sopra i 3 €).
///
/// Si confrontano solo le vendite recenti (<see cref="WindowDays"/>) con l'ultimo trend: il trend
/// delle singole cala nelle settimane dopo l'uscita, e confrontare vendite vecchie con il trend di
/// oggi gonfierebbe il rapporto. Sotto <see cref="MinTrend"/> l'abbinamento automatico fra Card Trader
/// e Cardmarket è troppo rumoroso (varianti abbinate al prodotto sbagliato), e quelle carte sono
/// comunque governate dalle regole del bulk.
/// </summary>
public class PriceRealizationService
{
    public const int WindowDays = 30;
    public const decimal MinTrend = 1m;

    /// <summary>Sotto questo valore di trend venduto il campione è troppo piccolo: si usa 1.</summary>
    private const decimal MinSampleTrend = 100m;

    private readonly ApplicationDbContext _db;

    public PriceRealizationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PriceRealization> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-WindowDays);

        var sales = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Order.PaidAt >= since && oi.Blueprint != null && oi.Blueprint.CardMarketIds != null)
            .Select(oi => new { oi.Quantity, oi.Price, oi.IsFoil, oi.Blueprint!.CardMarketIds })
            .ToListAsync(cancellationToken);

        var rows = sales
            .Select(s => new { s.Quantity, s.Price, s.IsFoil, CmId = FirstCardmarketId(s.CardMarketIds) })
            .Where(s => s.CmId.HasValue)
            .ToList();

        var ids = rows.Select(r => r.CmId!.Value).Distinct().ToList();
        var trends = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => ids.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, cancellationToken);

        decimal revenue = 0, trendValue = 0;
        var copies = 0;
        foreach (var row in rows)
        {
            if (!trends.TryGetValue(row.CmId!.Value, out var price)) continue;
            var trend = row.IsFoil ? price.TrendFoil : price.Trend;
            if (trend is not { } t || t < MinTrend) continue;

            revenue += row.Quantity * row.Price;
            trendValue += row.Quantity * t;
            copies += row.Quantity;
        }

        var measured = trendValue >= MinSampleTrend;
        return new PriceRealization(
            measured ? Math.Round(revenue / trendValue, 3) : 1m,
            measured, copies, Math.Round(revenue, 2), Math.Round(trendValue, 2));
    }

    /// <summary><see cref="Domain.Entities.Blueprint.CardMarketIds"/> è un array JSON: si prende il primo id.</summary>
    public static int? FirstCardmarketId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                   && doc.RootElement[0].TryGetInt32(out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <param name="Factor">Incassato / trend CM (1,17 = si incassa il 17% in più del trend).</param>
/// <param name="Measured">False se il campione era troppo piccolo e si usa 1.</param>
public record PriceRealization(decimal Factor, bool Measured, int Copies, decimal Revenue, decimal TrendValue);
