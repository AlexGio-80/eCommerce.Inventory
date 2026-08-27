using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Api.Controllers;

[ApiController]
[Route("api/pricing")]
[Authorize]
public class AutoPricingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AutoPricingService _pricingService;
    private readonly ILogger<AutoPricingController> _logger;

    public AutoPricingController(
        ApplicationDbContext context,
        AutoPricingService pricingService,
        ILogger<AutoPricingController> logger)
    {
        _context = context;
        _pricingService = pricingService;
        _logger = logger;
    }

    // --- Profili e regole ---

    [HttpGet("profiles")]
    public async Task<IActionResult> GetProfiles(CancellationToken cancellationToken)
    {
        var profiles = await _context.PricingProfiles
            .AsNoTracking()
            .Include(p => p.Rules)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<object>.SuccessResult(profiles.Select(MapProfile)));
    }

    [HttpGet("profiles/{id:int}")]
    public async Task<IActionResult> GetProfile(int id, CancellationToken cancellationToken)
    {
        var profile = await _context.PricingProfiles
            .AsNoTracking()
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile == null) return NotFound(ApiResponse<object>.ErrorResult($"Profilo {id} non trovato"));

        return Ok(ApiResponse<object>.SuccessResult(MapProfile(profile)));
    }

    [HttpPut("profiles/{id:int}")]
    public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await _context.PricingProfiles
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile == null) return NotFound(ApiResponse<object>.ErrorResult($"Profilo {id} non trovato"));

        profile.Name = request.Name ?? profile.Name;
        profile.IsActive = request.IsActive ?? profile.IsActive;
        profile.DryRun = request.DryRun ?? profile.DryRun;
        profile.MinPrice = request.MinPrice ?? profile.MinPrice;
        profile.MaxChangePercentPerRun = request.MaxChangePercentPerRun ?? profile.MaxChangePercentPerRun;
        profile.IncludeProSellers = request.IncludeProSellers ?? profile.IncludeProSellers;
        profile.IncludeNormalSellers = request.IncludeNormalSellers ?? profile.IncludeNormalSellers;
        profile.ExcludeVacationSellers = request.ExcludeVacationSellers ?? profile.ExcludeVacationSellers;
        profile.MinSellerDailyCapacity = request.MinSellerDailyCapacity ?? profile.MinSellerDailyCapacity;
        profile.CountryCodesCsv = request.CountryCodesCsv ?? profile.CountryCodesCsv;
        profile.EnableOutlierRejection = request.EnableOutlierRejection ?? profile.EnableOutlierRejection;
        profile.OutlierMadThreshold = request.OutlierMadThreshold ?? profile.OutlierMadThreshold;
        profile.MinComparableOffers = request.MinComparableOffers ?? profile.MinComparableOffers;
        profile.UpdatedAt = DateTime.UtcNow;

        // Se arrivano le regole, sostituiscono integralmente quelle esistenti:
        // un merge parziale renderebbe difficile capire quale insieme è attivo.
        if (request.Rules != null)
        {
            _context.PricingRules.RemoveRange(profile.Rules);
            foreach (var r in request.Rules)
            {
                profile.Rules.Add(new PricingRule
                {
                    FromPrice = r.FromPrice,
                    ToPrice = r.ToPrice,
                    ReferenceMode = r.ReferenceMode,
                    Position = r.Position,
                    AdjustmentAmount = r.AdjustmentAmount,
                    AdjustmentPercent = r.AdjustmentPercent,
                    CanIncrease = r.CanIncrease,
                    CanDecrease = r.CanDecrease,
                    Priority = r.Priority,
                    IsActive = r.IsActive
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.SuccessResult(MapProfile(profile), "Profilo aggiornato"));
    }

    // --- Anteprima ---

    /// <summary>
    /// Calcola cosa succederebbe senza scrivere nulla, né su Card Trader né sullo storico.
    /// È lo strumento per tarare le regole: ogni riga riporta il prezzo proposto e il perché.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(request.ProfileId, cancellationToken);
        if (profile == null) return NotFound(ApiResponse<object>.ErrorResult("Nessun profilo di pricing disponibile"));

        var blueprintIds = request.BlueprintIds?.ToList() ?? new List<int>();

        if (blueprintIds.Count == 0)
        {
            // Nessun blueprint indicato: si campionano le carte di maggior valore,
            // che sono quelle su cui conviene verificare per prime le regole.
            var limit = Math.Clamp(request.Limit ?? 25, 1, 200);
            blueprintIds = await _context.InventoryItems
                .AsNoTracking()
                .Where(i => i.Quantity > 0)
                .GroupBy(i => i.BlueprintId)
                .Select(g => new { BlueprintId = g.Key, MaxPrice = g.Max(i => i.ListingPrice) })
                .OrderByDescending(x => x.MaxPrice)
                .Take(limit)
                .Select(x => x.BlueprintId)
                .ToListAsync(cancellationToken);
        }

        var run = await _pricingService.RunAsync(
            blueprintIds, profile, PricingTrigger.Preview,
            forceDryRun: true, refreshPricesFirst: true, cancellationToken);

        return Ok(ApiResponse<object>.SuccessResult(await BuildRunReportAsync(run, cancellationToken)));
    }

    // --- Esecuzione ---

    /// <summary>
    /// Esegue l'autopricer su richiesta. Scrive su Card Trader solo se il profilo
    /// non è in dry-run: la modalità è una proprietà del profilo, non di questa chiamata.
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunRequest request, CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(request.ProfileId, cancellationToken);
        if (profile == null) return NotFound(ApiResponse<object>.ErrorResult("Nessun profilo di pricing disponibile"));

        var blueprintIds = request.BlueprintIds?.ToList()
            ?? await _pricingService.SelectBlueprintsForScheduledRunAsync(
                request.HighValueThreshold ?? 1.00m,
                request.BulkSliceSize ?? 0,
                cancellationToken);

        var run = await _pricingService.RunAsync(
            blueprintIds, profile, PricingTrigger.Manual,
            forceDryRun: false, refreshPricesFirst: true, cancellationToken);

        return Ok(ApiResponse<object>.SuccessResult(await BuildRunReportAsync(run, cancellationToken)));
    }

    // --- Storico e copertura ---

    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns([FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var runs = await _context.PricingRunLogs
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<object>.SuccessResult(runs.Select(r => new
        {
            r.Id,
            r.Trigger,
            r.DryRun,
            r.StartedAt,
            r.CompletedAt,
            r.PlannedCount,
            r.EvaluatedCount,
            r.AppliedCount,
            r.SimulatedCount,
            r.NoChangeCount,
            r.SkippedCount,
            r.FailedCount,
            r.TotalPriceDelta,
            r.CoveragePercent,
            r.ErrorMessage
        })));
    }

    [HttpGet("runs/{id:int}/changes")]
    public async Task<IActionResult> GetRunChanges(int id, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
    {
        var changes = await _context.PriceChangeLogs
            .AsNoTracking()
            .Where(c => c.PricingRunLogId == id)
            .Include(c => c.Blueprint)
            .OrderByDescending(c => Math.Abs(c.ProposedPrice - c.OldPrice))
            .Take(Math.Clamp(limit, 1, 2000))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<object>.SuccessResult(changes.Select(MapChange)));
    }

    /// <summary>
    /// Copertura del magazzino: da quanto tempo ogni fascia di prezzo non viene valutata.
    /// È la risposta misurabile al difetto "non aggiorna sempre tutte le carte".
    /// </summary>
    [HttpGet("coverage")]
    public async Task<IActionResult> GetCoverage(CancellationToken cancellationToken)
    {
        var perBlueprint = await _context.InventoryItems
            .AsNoTracking()
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.BlueprintId)
            .Select(g => new { BlueprintId = g.Key, MaxPrice = g.Max(i => i.ListingPrice) })
            .ToListAsync(cancellationToken);

        var lastEvaluated = await _context.PriceChangeLogs
            .AsNoTracking()
            .GroupBy(c => c.BlueprintId)
            .Select(g => new { BlueprintId = g.Key, LastAt = g.Max(c => c.CreatedAt) })
            .ToDictionaryAsync(x => x.BlueprintId, x => x.LastAt, cancellationToken);

        var now = DateTime.UtcNow;

        var bands = perBlueprint
            .GroupBy(x => x.MaxPrice <= 1m ? "0,02 - 1,00 (bulk)"
                : x.MaxPrice <= 25m ? "1,01 - 25,00"
                : x.MaxPrice <= 100m ? "25,01 - 100,00"
                : "oltre 100,00")
            .Select(g => new
            {
                Fascia = g.Key,
                Blueprint = g.Count(),
                MaiValutati = g.Count(x => !lastEvaluated.ContainsKey(x.BlueprintId)),
                ValutatiUltime24h = g.Count(x => lastEvaluated.TryGetValue(x.BlueprintId, out var t) && (now - t).TotalHours <= 24),
                ValutatiUltimi7Giorni = g.Count(x => lastEvaluated.TryGetValue(x.BlueprintId, out var t) && (now - t).TotalDays <= 7)
            })
            .OrderBy(x => x.Fascia)
            .ToList();

        return Ok(ApiResponse<object>.SuccessResult(new
        {
            BlueprintTotali = perBlueprint.Count,
            MaiValutati = perBlueprint.Count(x => !lastEvaluated.ContainsKey(x.BlueprintId)),
            Fasce = bands
        }));
    }

    // --- Helper ---

    private async Task<PricingProfile?> LoadProfileAsync(int? profileId, CancellationToken cancellationToken)
    {
        var query = _context.PricingProfiles.Include(p => p.Rules).AsQueryable();

        return profileId.HasValue
            ? await query.FirstOrDefaultAsync(p => p.Id == profileId.Value, cancellationToken)
            : await query.FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
    }

    private async Task<object> BuildRunReportAsync(PricingRunLog run, CancellationToken cancellationToken)
    {
        // In anteprima le variazioni vivono in memoria; in esecuzione reale sono a database.
        var changes = run.Changes.Count > 0
            ? run.Changes.ToList()
            : await _context.PriceChangeLogs
                .AsNoTracking()
                .Where(c => c.PricingRunLogId == run.Id)
                .Include(c => c.Blueprint)
                .ToListAsync(cancellationToken);

        // Per l'anteprima i blueprint non sono caricati: si risolvono i nomi in un colpo solo.
        var missingNames = changes.Where(c => c.Blueprint == null).Select(c => c.BlueprintId).Distinct().ToList();
        var names = missingNames.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Blueprints
                .AsNoTracking()
                .Where(b => missingNames.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return new
        {
            run.Trigger,
            run.DryRun,
            run.StartedAt,
            run.CompletedAt,
            run.PlannedCount,
            run.EvaluatedCount,
            run.AppliedCount,
            run.SimulatedCount,
            run.NoChangeCount,
            run.SkippedCount,
            run.FailedCount,
            run.TotalPriceDelta,
            run.CoveragePercent,
            Changes = changes
                .OrderByDescending(c => Math.Abs(c.ProposedPrice - c.OldPrice))
                .Select(c => MapChange(c, names))
                .ToList()
        };
    }

    private static object MapChange(PriceChangeLog c) => MapChange(c, new Dictionary<int, string>());

    private static object MapChange(PriceChangeLog c, IReadOnlyDictionary<int, string> fallbackNames) => new
    {
        c.Id,
        c.BlueprintId,
        CardName = c.Blueprint?.Name ?? (fallbackNames.TryGetValue(c.BlueprintId, out var n) ? n : null),
        c.InventoryItemId,
        c.OldPrice,
        c.ProposedPrice,
        Delta = c.ProposedPrice - c.OldPrice,
        c.ReferencePrice,
        c.ComparableOffersCount,
        c.OutliersRejectedCount,
        Outcome = c.Outcome.ToString(),
        c.Reason,
        c.CreatedAt
    };

    private static object MapProfile(PricingProfile p) => new
    {
        p.Id,
        p.Name,
        p.IsActive,
        p.DryRun,
        p.MinPrice,
        p.MaxChangePercentPerRun,
        p.IncludeProSellers,
        p.IncludeNormalSellers,
        p.ExcludeVacationSellers,
        p.MinSellerDailyCapacity,
        p.CountryCodesCsv,
        p.EnableOutlierRejection,
        p.OutlierMadThreshold,
        p.MinOffersForOutlierRejection,
        p.MinComparableOffers,
        p.MatchCondition,
        p.MatchLanguage,
        p.MatchFoil,
        Rules = p.Rules.OrderBy(r => r.FromPrice).Select(r => new
        {
            r.Id,
            r.FromPrice,
            r.ToPrice,
            ReferenceMode = r.ReferenceMode.ToString(),
            r.Position,
            r.AdjustmentAmount,
            r.AdjustmentPercent,
            r.CanIncrease,
            r.CanDecrease,
            r.Priority,
            r.IsActive
        })
    };
}

public class PreviewRequest
{
    public int? ProfileId { get; set; }
    public List<int>? BlueprintIds { get; set; }

    /// <summary>Quante carte campionare se non se ne indicano di specifiche.</summary>
    public int? Limit { get; set; }
}

public class RunRequest
{
    public int? ProfileId { get; set; }
    public List<int>? BlueprintIds { get; set; }
    public decimal? HighValueThreshold { get; set; }
    public int? BulkSliceSize { get; set; }
}

public class UpdateProfileRequest
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
    public bool? DryRun { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxChangePercentPerRun { get; set; }
    public bool? IncludeProSellers { get; set; }
    public bool? IncludeNormalSellers { get; set; }
    public bool? ExcludeVacationSellers { get; set; }
    public int? MinSellerDailyCapacity { get; set; }
    public string? CountryCodesCsv { get; set; }
    public bool? EnableOutlierRejection { get; set; }
    public decimal? OutlierMadThreshold { get; set; }
    public int? MinComparableOffers { get; set; }
    public List<RuleRequest>? Rules { get; set; }
}

public class RuleRequest
{
    public decimal FromPrice { get; set; }
    public decimal ToPrice { get; set; }
    public PriceReferenceMode ReferenceMode { get; set; } = PriceReferenceMode.NthLowestOffer;
    public int Position { get; set; } = 1;
    public decimal AdjustmentAmount { get; set; }
    public decimal AdjustmentPercent { get; set; }
    public bool CanIncrease { get; set; } = true;
    public bool CanDecrease { get; set; } = true;
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}
