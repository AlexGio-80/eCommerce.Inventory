using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Classifica delle opportunità sui sigillati di tutte le uscite (Fase 3 dell'analisi acquisti):
/// valore atteso dell'apertura contro prezzo d'acquisto, calcolato ogni giorno e salvato in
/// <see cref="SealedOpportunity"/>, insieme all'andamento del prezzo del sigillato.
/// </summary>
public class SealedOpportunityService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly SealedProductAnalysisService _analysis;
    private readonly ILogger<SealedOpportunityService> _logger;

    public SealedOpportunityService(
        ApplicationDbContext db,
        SealedProductAnalysisService analysis,
        ILogger<SealedOpportunityService> logger)
    {
        _db = db;
        _analysis = analysis;
        _logger = logger;
    }

    /// <summary>
    /// Ricalcola la classifica di oggi per tutte le uscite di cui sono stati scaricati i dati delle
    /// buste. Sostituisce le righe di oggi: si può rilanciare senza duplicare.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se un altro calcolo è in corso.</exception>
    public async Task<int> ComputeAsync(CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Il calcolo della classifica delle opportunità è già in corso");

        try
        {
            var started = DateTime.UtcNow;
            var sets = await _db.MtgjsonSets.AsNoTracking().ToListAsync(cancellationToken);
            var codes = sets.Select(s => s.Code).ToHashSet();
            var mainCodes = sets
                .Where(s => (s.ParentCode == null || !codes.Contains(s.ParentCode)) && s.DetailImportedAt != null)
                .Select(s => s.Code)
                .ToList();

            var analyses = await _analysis.AnalyzeManyAsync(mainCodes, cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.Today);
            _db.SealedOpportunities.RemoveRange(await _db.SealedOpportunities.Where(o => o.Date == today).ToListAsync(cancellationToken));

            var rows = analyses
                .SelectMany(a => a.Products
                    .Where(p => p.Decision != null && !p.IsCase)
                    .Select(p => new SealedOpportunity
                    {
                        Date = today,
                        SealedProductId = p.Id,
                        MainSetCode = a.Code,
                        CmTrend = p.CmTrend,
                        CmLow = p.CmLow,
                        OpenValueCm = p.OpenValueCm,
                        CoverageCm = p.CoverageCm,
                        SealedNetCm = p.SealedNetCm,
                        OpeningRoiPercent = p.OpeningRoiPercent,
                        Decision = p.Decision,
                        ComputedAt = started
                    }))
                .ToList();

            _db.SealedOpportunities.AddRange(rows);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Classifica opportunità sigillati: {Rows} prodotti da {Sets} uscite in {Seconds:F0} s",
                rows.Count, analyses.Count, (DateTime.UtcNow - started).TotalSeconds);

            return rows.Count;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Ultima classifica, con l'andamento del prezzo del sigillato (dallo storico Cardmarket) e del
    /// valore atteso (dalle classifiche dei giorni precedenti) a 7 e 30 giorni, quando disponibili.
    /// </summary>
    public async Task<OpportunityList> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var latest = await _db.SealedOpportunities.AsNoTracking()
            .Select(o => (DateOnly?)o.Date)
            .MaxAsync(cancellationToken);
        if (latest == null) return new OpportunityList(null, null, new List<OpportunityDto>());

        var rows = await _db.SealedOpportunities.AsNoTracking()
            .Where(o => o.Date == latest)
            .Select(o => new
            {
                o.SealedProductId, o.MainSetCode, o.CmTrend, o.CmLow, o.OpenValueCm, o.CoverageCm, o.SealedNetCm,
                o.OpeningRoiPercent, o.Decision, o.ComputedAt,
                Product = new { o.SealedProduct!.Name, o.SealedProduct.Category, o.SealedProduct.Subtype, o.SealedProduct.SetCode, o.SealedProduct.CardmarketId, o.SealedProduct.CardTraderBlueprintId }
            })
            .ToListAsync(cancellationToken);

        var setInfo = await _db.MtgjsonSets.AsNoTracking()
            .Select(s => new { s.Code, s.Name, s.ReleaseDate })
            .ToDictionaryAsync(s => s.Code, cancellationToken);

        var date7 = latest.Value.AddDays(-7);
        var date30 = latest.Value.AddDays(-30);
        var trendBefore = await TrendsAtAsync(rows.Where(r => r.Product.CardmarketId.HasValue).Select(r => r.Product.CardmarketId!.Value).ToList(),
            date7, date30, cancellationToken);

        var productIds = rows.Select(r => r.SealedProductId).ToList();
        var valueBefore = await _db.SealedOpportunities.AsNoTracking()
            .Where(o => productIds.Contains(o.SealedProductId) && (o.Date == date7 || o.Date == date30))
            .Select(o => new { o.SealedProductId, o.Date, o.OpenValueCm })
            .ToListAsync(cancellationToken);

        var result = rows.Select(r =>
        {
            setInfo.TryGetValue(r.MainSetCode, out var set);
            var cmId = r.Product.CardmarketId;
            decimal? trend7 = cmId.HasValue ? trendBefore.GetValueOrDefault((cmId.Value, date7)) : null;
            decimal? trend30 = cmId.HasValue ? trendBefore.GetValueOrDefault((cmId.Value, date30)) : null;
            var value7 = valueBefore.FirstOrDefault(v => v.SealedProductId == r.SealedProductId && v.Date == date7)?.OpenValueCm;
            var value30 = valueBefore.FirstOrDefault(v => v.SealedProductId == r.SealedProductId && v.Date == date30)?.OpenValueCm;

            return new OpportunityDto(
                r.SealedProductId, r.Product.Name, r.Product.Category, r.Product.Subtype,
                r.MainSetCode, set?.Name ?? r.MainSetCode, set?.ReleaseDate,
                r.CmTrend, r.CmLow, r.OpenValueCm, r.CoverageCm, r.SealedNetCm, r.OpeningRoiPercent, r.Decision,
                Change(r.CmTrend, trend7), Change(r.CmTrend, trend30),
                Change(r.OpenValueCm, value7), Change(r.OpenValueCm, value30),
                r.Product.CardTraderBlueprintId);
        })
        .OrderByDescending(o => o.OpeningRoiPercent ?? decimal.MinValue)
        .ToList();

        return new OpportunityList(latest, rows.Max(r => r.ComputedAt), result);
    }

    /// <summary>Trend dei sigillati in due date passate, dallo storico giornaliero Cardmarket.</summary>
    private async Task<Dictionary<(int, DateOnly), decimal?>> TrendsAtAsync(
        List<int> ids, DateOnly date7, DateOnly date30, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return new();
        return await _db.CardmarketPriceSnapshots.AsNoTracking()
            .Where(s => ids.Contains(s.IdProduct) && (s.Date == date7 || s.Date == date30))
            .ToDictionaryAsync(s => (s.IdProduct, s.Date), s => s.Trend, cancellationToken);
    }

    private static decimal? Change(decimal? now, decimal? before) =>
        now is { } n && before is > 0 ? Math.Round((n - before.Value) / before.Value * 100m, 1) : null;
}

public record OpportunityList(DateOnly? Date, DateTime? ComputedAt, List<OpportunityDto> Items);

/// <param name="TrendChange7">Variazione % del trend del sigillato rispetto a 7 giorni prima.</param>
/// <param name="ValueChange7">Variazione % del valore atteso rispetto a 7 giorni prima.</param>
public record OpportunityDto(
    int SealedProductId,
    string Name,
    string? Category,
    string? Subtype,
    string MainSetCode,
    string SetName,
    DateOnly? ReleaseDate,
    decimal? CmTrend,
    decimal? CmLow,
    decimal? OpenValueCm,
    decimal? CoverageCm,
    decimal? SealedNetCm,
    decimal? OpeningRoiPercent,
    string? Decision,
    decimal? TrendChange7,
    decimal? TrendChange30,
    decimal? ValueChange7,
    decimal? ValueChange30,
    int? CardTraderBlueprintId);
