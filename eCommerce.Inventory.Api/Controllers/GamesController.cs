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
    public async Task<ActionResult<Models.ApiResponse<List<GameDto>>>> GetGames(CancellationToken cancellationToken = default)
    {
        var games = await _dbContext.Games
            .OrderBy(g => g.Name)
            .Select(g => new GameDto
            {
                Id = g.Id,
                CardTraderId = g.CardTraderId,
                Name = g.Name,
                Code = g.Code,
                IsEnabled = g.IsEnabled
            })
            .ToListAsync(cancellationToken);

        return Ok(Models.ApiResponse<List<GameDto>>.SuccessResult(games));
    }

    /// <summary>
    /// Get a single game by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Models.ApiResponse<GameDto>>> GetGame(int id, CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games
            .Where(g => g.Id == id)
            .Select(g => new GameDto
            {
                Id = g.Id,
                CardTraderId = g.CardTraderId,
                Name = g.Name,
                Code = g.Code,
                IsEnabled = g.IsEnabled
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (game == null)
        {
            return NotFound(Models.ApiResponse<GameDto>.ErrorResult($"Game with ID {id} not found"));
        }

        return Ok(Models.ApiResponse<GameDto>.SuccessResult(game));
    }

    /// <summary>
    /// Update game details (specifically IsEnabled)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Models.ApiResponse<GameDto>>> UpdateGame(int id, [FromBody] UpdateGameDto dto, CancellationToken cancellationToken = default)
    {
        var game = await _dbContext.Games.FindAsync(new object[] { id }, cancellationToken);

        if (game == null)
        {
            return NotFound(Models.ApiResponse<GameDto>.ErrorResult($"Game with ID {id} not found"));
        }

        game.IsEnabled = dto.IsEnabled;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = new GameDto
        {
            Id = game.Id,
            CardTraderId = game.CardTraderId,
            Name = game.Name,
            Code = game.Code,
            IsEnabled = game.IsEnabled
        };

        return Ok(Models.ApiResponse<GameDto>.SuccessResult(result, "Game updated successfully"));
    }

    /// <summary>
    /// Trigger sync of expansions for this game
    /// Note: Currently triggers global expansion sync as API doesn't support filtering by game
    /// </summary>
    [HttpPost("{id}/sync-expansions")]
    public async Task<ActionResult<Models.ApiResponse<object>>> SyncExpansions(int id, CancellationToken cancellationToken = default)
    {
        // Verify game exists
        var game = await _dbContext.Games.FindAsync(new object[] { id }, cancellationToken);
        if (game == null)
        {
            return NotFound(Models.ApiResponse<object>.ErrorResult($"Game with ID {id} not found"));
        }

        // Trigger global expansion sync
        var result = await _syncOrchestrator.SyncAsync(new SyncRequestDto { SyncExpansions = true }, cancellationToken);

        return Ok(Models.ApiResponse<object>.SuccessResult(
            new { message = "Expansions sync completed", details = result },
            "Expansions sync completed successfully"));
    }

    /// <summary>
    /// Trigger full sync for this game (Expansions + Blueprints)
    /// </summary>
    [HttpPost("{id}/sync-all")]
    public async Task<ActionResult<Models.ApiResponse<object>>> SyncAll(int id, CancellationToken cancellationToken = default)
    {
        // Verify game exists
        var game = await _dbContext.Games.FindAsync(new object[] { id }, cancellationToken);
        if (game == null)
        {
            return NotFound(Models.ApiResponse<object>.ErrorResult($"Game with ID {id} not found"));
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
            var syncResult = await _syncOrchestrator.SyncBlueprintsForExpansionAsync(expansion.Id, cancellationToken);
            totalAdded += syncResult.Added;
            totalUpdated += syncResult.Updated;
            totalFailed += syncResult.Failed;
        }

        var responseData = new
        {
            message = $"Full sync completed for game {game.Name}",
            expansionsSynced = true,
            blueprintsStats = new { Added = totalAdded, Updated = totalUpdated, Failed = totalFailed }
        };

        return Ok(Models.ApiResponse<object>.SuccessResult(responseData, $"Full sync completed for {game.Name}"));
    }
}

public class GameDto
{
    public int Id { get; set; }
    public int? CardTraderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class UpdateGameDto
{
    public bool IsEnabled { get; set; }
}
