using Microsoft.AspNetCore.Mvc;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Api.Controllers.CardTrader;

/// <summary>
/// API controller for managing Card Trader blueprints (cards)
/// Provides endpoints for querying and managing card metadata
/// </summary>
[ApiController]
[Route("api/cardtrader/blueprints")]
public class CardTraderBlueprintsController : ControllerBase
{
    private readonly IBlueprintRepository _blueprintRepository;
    private readonly ILogger<CardTraderBlueprintsController> _logger;

    public CardTraderBlueprintsController(
        IBlueprintRepository blueprintRepository,
        ILogger<CardTraderBlueprintsController> logger)
    {
        _blueprintRepository = blueprintRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all blueprints with pagination
    /// </summary>
    /// <param name="page">Page number (default 1)</param>
    /// <param name="pageSize">Items per page (default 20, max 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged list of blueprints</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<Blueprint>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<Blueprint>>> GetAllBlueprints(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting blueprints - Page: {Page}, PageSize: {PageSize}", page, pageSize);

        var result = await _blueprintRepository.GetPagedAsync(page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get a specific blueprint by ID
    /// </summary>
    /// <param name="id">Blueprint ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Blueprint), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Blueprint>> GetBlueprintById(
        int id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting blueprint {BlueprintId}", id);

        var blueprint = await _blueprintRepository.GetByIdAsync(id, cancellationToken);
        if (blueprint == null)
        {
            _logger.LogWarning("Blueprint {BlueprintId} not found", id);
            return NotFound(new { message = $"Blueprint with ID {id} not found" });
        }

        return Ok(blueprint);
    }

    /// <summary>
    /// Get blueprints by game ID
    /// </summary>
    /// <param name="gameId">Game ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of blueprints for the specified game</returns>
    [HttpGet("by-game/{gameId}")]
    [ProducesResponseType(typeof(IEnumerable<Blueprint>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Blueprint>>> GetBlueprintsByGame(
        int gameId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting blueprints for game {GameId}", gameId);

        var blueprints = await _blueprintRepository.GetByGameIdAsync(gameId, cancellationToken);
        return Ok(blueprints);
    }

    /// <summary>
    /// Get blueprints by expansion ID
    /// </summary>
    /// <param name="expansionId">Expansion ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of blueprints in the specified expansion</returns>
    [HttpGet("by-expansion/{expansionId}")]
    [ProducesResponseType(typeof(IEnumerable<Blueprint>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Blueprint>>> GetBlueprintsByExpansion(
        int expansionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting blueprints for expansion {ExpansionId}", expansionId);

        var blueprints = await _blueprintRepository.GetByExpansionIdAsync(expansionId, cancellationToken);
        return Ok(blueprints);
    }

    /// <summary>
    /// Get blueprint by Card Trader ID
    /// Useful for checking if a blueprint already exists during synchronization
    /// </summary>
    /// <param name="cardTraderId">Card Trader ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint if found, 404 otherwise</returns>
    [HttpGet("by-cardtrader-id/{cardTraderId}")]
    [ProducesResponseType(typeof(Blueprint), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Blueprint>> GetBlueprintByCardTraderId(
        int cardTraderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting blueprint by Card Trader ID {CardTraderId}", cardTraderId);

        var blueprint = await _blueprintRepository.GetByCardTraderIdAsync(cardTraderId, cancellationToken);
        if (blueprint == null)
        {
            _logger.LogWarning("Blueprint with Card Trader ID {CardTraderId} not found", cardTraderId);
            return NotFound(new { message = $"Blueprint with Card Trader ID {cardTraderId} not found" });
        }

        return Ok(blueprint);
    }

    /// <summary>
    /// Search blueprints by name (partial match, case-insensitive)
    /// </summary>
    /// <param name="name">Blueprint name to search for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching blueprints (max 50)</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IEnumerable<Blueprint>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<Blueprint>>> SearchBlueprints(
        [FromQuery] string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Search term cannot be empty" });
        }

        _logger.LogInformation("Searching blueprints with name containing '{SearchTerm}'", name);

        var blueprints = await _blueprintRepository.SearchByNameAsync(name, cancellationToken);
        return Ok(blueprints);
    }

    /// <summary>
    /// Get total count of blueprints in the database
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total blueprint count</returns>
    [HttpGet("stats/count")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> GetBlueprintCount(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting total blueprint count");

        var count = await _blueprintRepository.GetCountAsync(cancellationToken);
        return Ok(count);
    }
    /// <summary>
    /// Get the next or previous blueprint based on collector number
    /// </summary>
    [HttpGet("adjacent")]
    [ProducesResponseType(typeof(Blueprint), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Blueprint>> GetAdjacentBlueprint(
        [FromQuery] int expansionId,
        [FromQuery] string currentCollectorNumber,
        [FromQuery] string direction, // "next" or "prev"
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentCollectorNumber))
        {
            return BadRequest(new { message = "Current collector number is required" });
        }

        _logger.LogInformation("Getting adjacent blueprint for expansion {ExpansionId}, current {Current}, direction {Direction}",
            expansionId, currentCollectorNumber, direction);

        // This logic is a bit complex because collector numbers can be alphanumeric (e.g. "123a", "A123")
        // For now, we'll fetch all blueprints for the expansion and sort them in memory or use a smart query if possible
        // Given the number of cards per expansion is usually manageable (< 500), fetching IDs and FixedProperties might be okay
        // But let's try to do it efficiently.

        // We need a repository method for this, but for now let's implement a basic version using the existing GetByExpansionIdAsync
        // and filtering/sorting in memory. Ideally this should be pushed to the database.

        var blueprints = await _blueprintRepository.GetByExpansionIdAsync(expansionId, cancellationToken);

        // Parse collector numbers and sort
        var sortedBlueprints = blueprints
            .Select(b => new
            {
                Blueprint = b,
                CollectorNumber = GetCollectorNumber(b.FixedProperties)
            })
            .Where(x => x.CollectorNumber != null)
            .OrderBy(x => PadNumbers(x.CollectorNumber)) // Simple alphanumeric sort
            .ToList();

        var currentIndex = sortedBlueprints.FindIndex(x => x.CollectorNumber == currentCollectorNumber);

        if (currentIndex == -1)
        {
            return NotFound(new { message = "Current collector number not found in expansion" });
        }

        int targetIndex = direction.ToLower() == "next" ? currentIndex + 1 : currentIndex - 1;

        if (targetIndex >= 0 && targetIndex < sortedBlueprints.Count)
        {
            return Ok(sortedBlueprints[targetIndex].Blueprint);
        }

        return NotFound(new { message = "No adjacent blueprint found" });
    }

    private string? GetCollectorNumber(string? fixedProperties)
    {
        if (string.IsNullOrEmpty(fixedProperties)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(fixedProperties);
            if (doc.RootElement.TryGetProperty("collector_number", out var prop))
            {
                return prop.GetString();
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        return null;
    }

    private string PadNumbers(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, "[0-9]+", match => match.Value.PadLeft(10, '0'));
    }
}
