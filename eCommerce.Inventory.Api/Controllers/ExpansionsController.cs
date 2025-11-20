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
    private readonly ILogger<ExpansionsController> _logger;

    public ExpansionsController(
        ApplicationDbContext dbContext,
        CardTraderSyncOrchestrator syncOrchestrator,
        ICardTraderApiService cardTraderApiService,
        ILogger<ExpansionsController> logger)
    {
        _dbContext = dbContext;
        _syncOrchestrator = syncOrchestrator;
        _cardTraderApiService = cardTraderApiService;
        _logger = logger;
    }

    /// <summary>
    /// Get all expansions with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetExpansions(
        [FromQuery] int? gameId = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
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
                .Select(e => new
                {
                    e.Id,
                    e.CardTraderId,
                    e.Name,
                    e.Code,
                    e.GameId,
                    GameName = e.Game.Name,
                    GameCode = e.Game.Code
                })
                .ToListAsync(cancellationToken);

            return Ok(expansions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching expansions");
            return StatusCode(500, new { error = "Failed to fetch expansions" });
        }
    }

    /// <summary>
    /// Get a single expansion by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetExpansion(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var expansion = await _dbContext.Expansions
                .Include(e => e.Game)
                .Where(e => e.Id == id)
                .Select(e => new
                {
                    e.Id,
                    e.CardTraderId,
                    e.Name,
                    e.Code,
                    e.GameId,
                    GameName = e.Game.Name,
                    GameCode = e.Game.Code
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (expansion == null)
            {
                return NotFound(new { error = "Expansion not found" });
            }

            return Ok(expansion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching expansion {ExpansionId}", id);
            return StatusCode(500, new { error = "Failed to fetch expansion" });
        }
    }

    /// <summary>
    /// Sync blueprints for a specific expansion
    /// </summary>
    [HttpPost("{id}/sync-blueprints")]
    public async Task<IActionResult> SyncBlueprints(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting blueprint sync for expansion {ExpansionId}", id);

            // Get the expansion
            var expansion = await _dbContext.Expansions
                .Include(e => e.Game)
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

            if (expansion == null)
            {
                return NotFound(new { error = "Expansion not found" });
            }

            _logger.LogInformation("Syncing blueprints for expansion {ExpansionName} (CardTraderId: {CardTraderId})",
                expansion.Name, expansion.CardTraderId);

            // Fetch blueprints from Card Trader API
            var blueprintDtos = await _cardTraderApiService.SyncBlueprintsForExpansionAsync(
                expansion.CardTraderId,
                cancellationToken);

            _logger.LogInformation("Fetched {Count} blueprints from API for expansion {ExpansionId}",
                blueprintDtos.Count(), expansion.CardTraderId);

            // TODO: Map and save blueprints
            // For now, just return the count
            return Ok(new
            {
                expansionId = id,
                expansionName = expansion.Name,
                cardTraderId = expansion.CardTraderId,
                blueprintsFetched = blueprintDtos.Count(),
                message = $"Fetched {blueprintDtos.Count()} blueprints from Card Trader API"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing blueprints for expansion {ExpansionId}", id);
            return StatusCode(500, new { error = $"Failed to sync blueprints: {ex.Message}" });
        }
    }
}
