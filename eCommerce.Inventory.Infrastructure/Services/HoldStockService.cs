using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Magazzino da tenere (Fase 3 della feature 005): gli acquisti segnati "da tenere" e ancora
/// sigillati, con quanto valgono oggi e il guadagno latente.
///
/// Il valore di un'unità è il migliore fra due uscite, entrambe al netto dei costi di vendita:
/// rivenderla chiusa al trend Cardmarket, o aprirla e vendere le carte (valore atteso della
/// classifica). Sono gli stessi numeri della scheda Opportunità, così i due conti non divergono.
/// </summary>
public class HoldStockService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public HoldStockService(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<HoldStockDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var purchases = await _db.ProductPurchases.AsNoTracking()
            .Where(p => p.HoldForResale && p.OpenedAt == null)
            .Select(p => new
            {
                p.Id, p.SealedProductId, p.Quantity, p.UnitPrice, p.PurchasedAt, p.CreatedAt, p.Store,
                p.SealedProduct!.Name, p.SealedProduct.SetCode, p.SealedProduct.CardmarketId,
                p.SealedProduct.CtMinPrice, p.SealedProduct.CardTraderBlueprintId
            })
            .ToListAsync(cancellationToken);
        if (purchases.Count == 0) return new HoldStockDto(new List<HoldStockItemDto>(), 0, 0, 0, null);

        var productIds = purchases.Select(p => p.SealedProductId).Distinct().ToList();
        var cmIds = purchases.Where(p => p.CardmarketId.HasValue).Select(p => p.CardmarketId!.Value).Distinct().ToList();

        // Ultima classifica: valore aperto e netto chiuso già calcolati con i parametri del modello.
        var lastDate = await _db.SealedOpportunities.Select(o => (DateOnly?)o.Date).MaxAsync(cancellationToken);
        var opportunities = lastDate == null ? new Dictionary<int, RankedValues>()
            : await _db.SealedOpportunities.AsNoTracking()
                .Where(o => o.Date == lastDate && productIds.Contains(o.SealedProductId))
                .ToDictionaryAsync(o => o.SealedProductId, o => new RankedValues(o.CmTrend, o.OpenValueCm, o.SealedNetCm), cancellationToken);

        // Prodotti fuori dalla classifica (es. senza dati delle buste): resta il trend del listino.
        var latest = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => cmIds.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, p => p.Trend, cancellationToken);

        // Trend alla data d'acquisto: l'ultima rilevazione fino a quel giorno, se lo storico ci arriva.
        var history = (await _db.CardmarketPriceSnapshots.AsNoTracking()
                .Where(s => cmIds.Contains(s.IdProduct) && s.Trend > 0)
                .Select(s => new { s.IdProduct, s.Date, s.Trend })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.IdProduct)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Date).Select(s => (s.Date, s.Trend!.Value)).ToList());

        var costPercent = _configuration.GetValue("Purchasing:SellingCostPercent", 15m);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var items = purchases.Select(p =>
        {
            var o = opportunities.GetValueOrDefault(p.SealedProductId) ?? new RankedValues(null, null, null);
            var trend = o.Trend ?? (p.CardmarketId is { } cm ? latest.GetValueOrDefault(cm) : null);
            var bought = p.PurchasedAt ?? DateOnly.FromDateTime(p.CreatedAt);
            var trendAtPurchase = p.CardmarketId is { } id && history.TryGetValue(id, out var series)
                ? TrendOn(series, bought)
                : null;

            var value = Value(p.Quantity, p.UnitPrice, trend, o.SealedNet, o.OpenNet, costPercent);
            return new HoldStockItemDto(
                p.Id, p.SealedProductId, p.Name, p.SetCode, p.Store, p.Quantity, p.UnitPrice, value.Cost,
                bought, MonthsHeld(bought, today),
                trend, value.SealedNetUnit, o.OpenNet, p.CtMinPrice,
                value.BestExit, value.ValueTotal, value.Gain, value.GainPercent,
                trendAtPurchase, Change(trend, trendAtPurchase), p.CardTraderBlueprintId);
        })
        .OrderByDescending(i => i.GainPercent ?? decimal.MinValue)
        .ToList();

        var totalCost = items.Sum(i => i.Cost);
        var valued = items.Where(i => i.ValueTotal.HasValue).ToList();
        var totalValue = valued.Sum(i => i.ValueTotal!.Value);
        var totalGain = valued.Sum(i => i.Gain!.Value);
        return new HoldStockDto(items, totalCost, totalValue, totalGain, lastDate);
    }

    /// <summary>
    /// Valore di oggi di un acquisto: la migliore fra rivendita chiusa e apertura, al netto dei costi
    /// di vendita. Senza la classifica il netto chiuso si calcola dal trend.
    /// </summary>
    public static HoldValue Value(int quantity, decimal unitPrice, decimal? trend, decimal? sealedNet, decimal? openNet, decimal costPercent)
    {
        var cost = Math.Round(quantity * unitPrice, 2);
        var sealedUnit = sealedNet ?? (trend is > 0 ? Math.Round(trend.Value * (1 - costPercent / 100m), 2) : null);

        HoldExit? best = (sealedUnit, openNet) switch
        {
            (null, null) => null,
            ({ } s, null) => HoldExit.Sealed,
            (null, { }) => HoldExit.Open,
            ({ } s, { } op) => op > s ? HoldExit.Open : HoldExit.Sealed
        };
        decimal? unit = best switch { HoldExit.Open => openNet, HoldExit.Sealed => sealedUnit, _ => null };
        if (unit is null) return new HoldValue(cost, sealedUnit, null, null, null, null);

        var total = Math.Round(quantity * unit.Value, 2);
        var gain = total - cost;
        return new HoldValue(cost, sealedUnit, best, total, gain, cost > 0 ? Math.Round(gain / cost * 100m, 1) : null);
    }

    /// <summary>Ultimo trend rilevato fino a quel giorno; null se lo storico parte dopo.</summary>
    public static decimal? TrendOn(List<(DateOnly Date, decimal Trend)> series, DateOnly day)
    {
        decimal? found = null;
        foreach (var (date, trend) in series)
        {
            if (date > day) break;
            found = trend;
        }
        return found;
    }

    public static int MonthsHeld(DateOnly bought, DateOnly today) => Math.Max(0, HoldScoreCalculator.AgeInMonths(bought, today));

    /// <summary>Valori di un prodotto nell'ultima classifica: trend, netto aprendolo, netto chiuso.</summary>
    private record RankedValues(decimal? Trend, decimal? OpenNet, decimal? SealedNet);

    private static decimal? Change(decimal? now, decimal? before) =>
        now is { } n && before is > 0 ? Math.Round((n - before.Value) / before.Value * 100m, 1) : null;
}

public enum HoldExit { Sealed, Open }

/// <param name="SealedNetUnit">Netto di un'unità rivenduta chiusa al trend.</param>
/// <param name="BestExit">L'uscita che vale di più oggi; null senza prezzi.</param>
public record HoldValue(decimal Cost, decimal? SealedNetUnit, HoldExit? BestExit, decimal? ValueTotal, decimal? Gain, decimal? GainPercent);

/// <param name="Date">Data della classifica da cui vengono i valori.</param>
/// <param name="TotalValue">Somma dei valori degli acquisti che hanno un prezzo.</param>
public record HoldStockDto(List<HoldStockItemDto> Items, decimal TotalCost, decimal TotalValue, decimal TotalGain, DateOnly? Date);

/// <param name="TrendAtPurchase">Trend Cardmarket alla data d'acquisto; null se lo storico (dal 2026-10-07) non ci arriva.</param>
public record HoldStockItemDto(
    int PurchaseId,
    int SealedProductId,
    string Name,
    string SetCode,
    string? Store,
    int Quantity,
    decimal UnitPrice,
    decimal Cost,
    DateOnly PurchasedAt,
    int MonthsHeld,
    decimal? CmTrend,
    decimal? SealedNetUnit,
    decimal? OpenNetUnit,
    decimal? CtMinPrice,
    HoldExit? BestExit,
    decimal? ValueTotal,
    decimal? Gain,
    decimal? GainPercent,
    decimal? TrendAtPurchase,
    decimal? TrendChangeSincePurchase,
    int? CardTraderBlueprintId);
