using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CardTraderSyncOrchestrator _syncOrchestrator;
    private readonly ILogger<GamesController> _logger;

    public GamesController(
        ApplicationDbContext dbContext,
        CardTraderSyncOrchestrator syncOrchestrator,
        ILogger<GamesController> logger)
    {
        _dbContext = dbContext;
        _syncOrchestrator = syncOrchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Get all games
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetGames(CancellationToken cancellationToken = default)
    {
        try
        {
            var games = await _dbContext.Games
                .OrderBy(g => g.Name)
                .Select(g => new
                {
                    g.Id,
                    g.CardTraderId,
                    g.Name,
                    g.Code,
                    g.IsEnabled
                })
                .ToListAsync(cancellationToken);

            return Ok(games);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching games");
            return StatusCode(500, new { error = "Failed to fetch games" });
        }
    }

    /// <summary>
    /// Get a single game by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetGame(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var game = await _dbContext.Games
                .Where(g => g.Id == id)
                .Select(g => new
                {
                    g.Id,
                    g.CardTraderId,
                    g.Name,
                    g.Code,
                    g.IsEnabled
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
            }

            return Ok(game);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching game {GameId}", id);
            return StatusCode(500, new { error = "Failed to fetch game" });
        }
    }

    /// <summary>
    /// Update game details (specifically IsEnabled)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateGame(int id, [FromBody] UpdateGameDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var game = await _dbContext.Games.FindAsync(new object[] { id }, cancellationToken);

            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
            }

            game.IsEnabled = dto.IsEnabled;

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                game.Id,
                game.CardTraderId,
                game.Name,
                game.Code,
                game.IsEnabled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating game {GameId}", id);
            return StatusCode(500, new { error = "Failed to update game" });
        }
    }

    /// <summary>
    /// Trigger sync of expansions for this game
    /// Note: Currently triggers global expansion sync as API doesn't support filtering by game
    /// </summary>
    [HttpPost("{id}/sync-expansions")]
    public async Task<IActionResult> SyncExpansions(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify game exists
            var game = await _dbContext.Games.FindAsync(new object[] { id }, cancellationToken);
            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
            }

            // Trigger global expansion sync
            var result = await _syncOrchestrator.SyncAsync(new SyncRequestDto { SyncExpansions = true }, cancellationToken);

            return Ok(new
            {
                message = "Expansions sync completed",
                details = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing expansions for game {GameId}", id);
            return StatusCode(500, new { error = "Failed to sync expansions" });
        }
    }

    /// <summary>
    /// Trigger full sync for this game (Expansions + Blueprints)
    /// </summary>
    [HttpPost("{id}/sync-all")]
    public async Task<IActionResult> SyncAll(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify game exists
            var game = await _dbContext.Games.FindAsync(new object[] { id }, cancellationToken);
            if (game == null)
            {
                return NotFound(new { error = "Game not found" });
            }

            // 1. Sync Expansions (Global)
            await _syncOrchestrator.SyncAsync(new SyncRequestDto { SyncExpansions = true }, cancellationToken);

            // 2. Sync Blueprints for all expansions of this game
            var expansions = await _dbContext.Expansions
                .Where(e => e.GameId == id)
                .ToListAsync(cancellationToken);

            int totalAdded = 0;
            int totalUpdated = 0;
            int totalFailed = 0;

            foreach (var expansion in expansions)
            {
                var result = await _syncOrchestrator.SyncBlueprintsForExpansionAsync(expansion.Id, cancellationToken);
                totalAdded += result.Added;
                totalUpdated += result.Updated;
                totalFailed += result.Failed;
            }

            return Ok(new
            {
                message = $"Full sync completed for game {game.Name}",
                expansionsSynced = true,
                blueprintsStats = new { Added = totalAdded, Updated = totalUpdated, Failed = totalFailed }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing full sync for game {GameId}", id);
            return StatusCode(500, new { error = "Failed to perform full sync" });
        }
    }
}

public class UpdateGameDto
{
    public bool IsEnabled { get; set; }
}
