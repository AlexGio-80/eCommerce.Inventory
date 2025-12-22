using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Get all expansions with optional filtering
    /// </summary>
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

        var expansions = await query
            .OrderBy(e => e.Game.Name)
            .ThenBy(e => e.Name)
            .Select(e => new ExpansionDto
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
                LastValueAnalysisUpdate = e.LastValueAnalysisUpdate
            })
            .ToListAsync(cancellationToken);

        return Ok(Models.ApiResponse<List<ExpansionDto>>.SuccessResult(expansions));
    }

    /// <summary>
    /// Get a single expansion by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Models.ApiResponse<ExpansionDto>>> GetExpansion(int id, CancellationToken cancellationToken = default)
    {
        var expansion = await _dbContext.Expansions
            .Include(e => e.Game)
            .Where(e => e.Id == id)
            .Select(e => new ExpansionDto
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
                LastValueAnalysisUpdate = e.LastValueAnalysisUpdate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (expansion == null)
        {
            return NotFound(Models.ApiResponse<ExpansionDto>.ErrorResult($"Expansion with ID {id} not found"));
        }

        return Ok(Models.ApiResponse<ExpansionDto>.SuccessResult(expansion));
    }

    /// <summary>
    /// Sync blueprints for a specific expansion
    /// </summary>
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

    /// <summary>
    /// Trigger value analysis for a specific expansion
    /// </summary>
    [HttpPost("{id}/analyze-value")]
    public async Task<ActionResult<Models.ApiResponse<object>>> AnalyzeValue(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            await _expansionAnalyticsService.AnalyzeExpansionValueAsync(id, cancellationToken);
            return Ok(Models.ApiResponse<object>.SuccessResult(null, "Value analysis completed successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during value analysis for expansion {ExpansionId}", id);
            return StatusCode(500, Models.ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    /// <summary>
    /// Trigger value analysis for all enabled expansions
    /// </summary>
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
}
