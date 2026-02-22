using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace eCommerce.Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExpansionsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CardTraderSyncOrchestrator _syncOrchestrator;
    private readonly ICardTraderApiService _cardTraderApiService;
    private readonly IExpansionAnalyticsService _expansionAnalyticsService;
    private readonly ILogger<ExpansionsController> _logger;

    public ExpansionsController(
        ApplicationDbContext dbContext,
        CardTraderSyncOrchestrator syncOrchestrator,
        ICardTraderApiService cardTraderApiService,
        IExpansionAnalyticsService expansionAnalyticsService,
        ILogger<ExpansionsController> logger)
    {
        _dbContext = dbContext;
        _syncOrchestrator = syncOrchestrator;
        _cardTraderApiService = cardTraderApiService;
        _expansionAnalyticsService = expansionAnalyticsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<Models.ApiResponse<List<ExpansionDto>>>> GetExpansions(
        [FromQuery] int? gameId = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Expansions
            .Include(e => e.Game)
            .AsQueryable();

        if (gameId.HasValue)
        {
            query = query.Where(e => e.GameId == gameId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e =>
                e.Name.Contains(search) ||
                e.Code.Contains(search));
        }

        // Efficiently join with ExpansionROI view using LINQ Query Syntax for reliable SQL translation
        // Use Left Join (DefaultIfEmpty) to include Expansions even if no ROI data exists
        var expansionsQuery = from e in query
                              join r in _dbContext.ExpansionsROI on e.Name equals r.ExpansionName into rois
                              from roi in rois.DefaultIfEmpty()
                              orderby e.Game.Name, e.Name
                              select new ExpansionDto
                              {
                                  Id = e.Id,
                                  CardTraderId = e.CardTraderId,
                                  Name = e.Name,
                                  Code = e.Code,
                                  GameId = e.GameId,
                                  GameName = e.Game.Name,
                                  GameCode = e.Game.Code,
                                  AverageCardValue = e.AverageCardValue,
                                  TotalMinPrice = e.TotalMinPrice,
                                  LastValueAnalysisUpdate = e.LastValueAnalysisUpdate,
                                  AvgValueCommon = e.AvgValueCommon,
                                  AvgValueUncommon = e.AvgValueUncommon,
                                  AvgValueRare = e.AvgValueRare,
                                  AvgValueMythic = e.AvgValueMythic,

                                  // Financials from ROI View - roi can be null from LEFT JOIN, properties handle nulls
                                  TotalSales = roi.TotaleVenduto ?? 0m,
                                  TotalProfit = roi.Differenza ?? 0m,
                                  TotalAmountSpent = roi.TotaleAcquistato ?? 0m,
                                  RoiPercentage = (roi.TotaleAcquistato ?? 0m) > 0
                                      ? ((roi.Differenza ?? 0m) / (roi.TotaleAcquistato ?? 0m)) * 100
                                      : 0m,
                                  ReleaseDate = e.ReleaseDate,
                                  IconSvgUri = e.IconSvgUri
                              };

        var expansions = await expansionsQuery.ToListAsync(cancellationToken);

        return Ok(Models.ApiResponse<List<ExpansionDto>>.SuccessResult(expansions));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Models.ApiResponse<ExpansionDto>>> GetExpansion(int id, CancellationToken cancellationToken = default)
    {
        var expansion = await _dbContext.Expansions
            .Include(e => e.Game)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (expansion == null)
        {
            return NotFound(Models.ApiResponse<ExpansionDto>.ErrorResult($"Expansion with ID {id} not found"));
        }

        var dto = new ExpansionDto
        {
            Id = expansion.Id,
            CardTraderId = expansion.CardTraderId,
            Name = expansion.Name,
            Code = expansion.Code,
            GameId = expansion.GameId,
            GameName = expansion.Game.Name,
            GameCode = expansion.Game.Code,
            AverageCardValue = expansion.AverageCardValue,
            TotalMinPrice = expansion.TotalMinPrice,
            LastValueAnalysisUpdate = expansion.LastValueAnalysisUpdate,
            AvgValueCommon = expansion.AvgValueCommon,
            AvgValueUncommon = expansion.AvgValueUncommon,
            AvgValueRare = expansion.AvgValueRare,
            AvgValueMythic = expansion.AvgValueMythic,
            ReleaseDate = expansion.ReleaseDate,
            IconSvgUri = expansion.IconSvgUri
        };

        try
        {
            var roi = await _dbContext.Set<Domain.Entities.ExpansionROI>()
                .FirstOrDefaultAsync(r => r.ExpansionName == expansion.Name, cancellationToken);

            if (roi != null)
            {
                decimal sales = roi.TotaleVenduto ?? 0m;
                decimal profit = roi.Differenza ?? 0m;
                decimal spent = roi.TotaleAcquistato ?? 0m;

                dto.TotalSales = sales;
                dto.TotalProfit = profit;
                dto.TotalAmountSpent = spent;

                // Calculate ROI %: (Profit / Cost) * 100
                if (spent > 0)
                {
                    dto.RoiPercentage = (profit / spent) * 100m;
                }
                else
                {
                    dto.RoiPercentage = 0m;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch ROI for expansion {ExpansionId}", id);
            // Non-blocking, just return DTO without ROI
        }

        return Ok(Models.ApiResponse<ExpansionDto>.SuccessResult(dto));
    }

    [HttpPost("{id}/sync-blueprints")]
    public async Task<ActionResult<Models.ApiResponse<object>>> SyncBlueprints(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _syncOrchestrator.SyncBlueprintsForExpansionAsync(id, cancellationToken);

            var responseData = new
            {
                expansionId = id,
                blueprintsAdded = result.Added,
                blueprintsUpdated = result.Updated,
                blueprintsFailed = result.Failed,
                message = $"Sync completed. Added: {result.Added}, Updated: {result.Updated}, Failed: {result.Failed}"
            };

            return Ok(Models.ApiResponse<object>.SuccessResult(
                responseData,
                $"Blueprints sync completed. Added: {result.Added}, Updated: {result.Updated}, Failed: {result.Failed}"));
        }
        catch (ArgumentException ex)
        {
            return NotFound(Models.ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpPost("{id}/analyze-value")]
    public async Task<ActionResult<Models.ApiResponse<object>>> AnalyzeValue(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _expansionAnalyticsService.AnalyzeExpansionValueAsync(id, cancellationToken);
            return Ok(Models.ApiResponse<object>.SuccessResult(result,
                $"Analisi completata: {result.CardsAnalyzedCount} carte analizzate su {result.BlueprintCount} blueprints. " +
                $"Valore medio: €{result.AverageValue:F2}"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Models.ApiResponse<object>.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during value analysis for expansion {ExpansionId}", id);
            return StatusCode(500, Models.ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpPost("analyze-all-values")]
    public async Task<ActionResult<Models.ApiResponse<object>>> AnalyzeAllValues(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _expansionAnalyticsService.AnalyzeAllExpansionsValueAsync(cancellationToken);
            return Ok(Models.ApiResponse<object>.SuccessResult(result, $"Value analysis completed. Success: {result.SuccessCount}, Failed: {result.FailedCount}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk value analysis");
            return StatusCode(500, Models.ApiResponse<object>.ErrorResult(ex.Message));
        }
    }
}

public class ExpansionDto
{
    public int Id { get; set; }
    public int? CardTraderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string GameCode { get; set; } = string.Empty;
    public decimal? AverageCardValue { get; set; }
    public decimal? TotalMinPrice { get; set; }
    public DateTime? LastValueAnalysisUpdate { get; set; }

    // Rarity Stats
    public decimal? AvgValueCommon { get; set; }
    public decimal? AvgValueUncommon { get; set; }
    public decimal? AvgValueRare { get; set; }
    public decimal? AvgValueMythic { get; set; }

    // Financials
    public decimal? TotalSales { get; set; }
    public decimal? TotalProfit { get; set; }
    public decimal? TotalAmountSpent { get; set; }
    public decimal? RoiPercentage { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? IconSvgUri { get; set; }
}
