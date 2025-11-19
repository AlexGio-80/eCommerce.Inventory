using Microsoft.AspNetCore.Mvc;
using eCommerce.Inventory.Infrastructure.Persistence;

namespace eCommerce.Inventory.Api.Controllers.CardTrader;

/// <summary>
/// Controller for database seeding operations (development/testing only)
/// WARNING: These endpoints should only be available in development environment
/// </summary>
[ApiController]
[Route("api/cardtrader/admin/seeding")]
public class CardTraderSeedingController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CardTraderSeedingController> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IWebHostEnvironment _env;

    public CardTraderSeedingController(
        ApplicationDbContext dbContext,
        ILogger<CardTraderSeedingController> logger,
        ILoggerFactory loggerFactory,
        IWebHostEnvironment env)
    {
        _dbContext = dbContext;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _env = env;
    }

    /// <summary>
    /// Import blueprints from JSON file
    /// WARNING: Only available in development environment
    /// </summary>
    /// <param name="filePath">Path to JSON file relative to project root (e.g., "Features/Blueprints-Data-Examples.json")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with counts</returns>
    [HttpPost("import-blueprints")]
    [ProducesResponseType(typeof(SeedingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SeedingResult>> ImportBlueprints(
        [FromQuery] string filePath = "Features/Blueprints-Data-Examples.json",
        CancellationToken cancellationToken = default)
    {
        // Only allow in development
        if (!_env.IsDevelopment())
        {
            _logger.LogWarning("Seeding endpoint accessed in non-development environment");
            return Forbid("Seeding operations are only available in development environment");
        }

        try
        {
            _logger.LogInformation("Starting blueprint import from {FilePath}", filePath);

            // Resolve the file path
            var fullPath = Path.Combine(_env.ContentRootPath, filePath);

            if (!System.IO.File.Exists(fullPath))
            {
                return BadRequest(new SeedingResult
                {
                    Success = false,
                    Message = $"File not found: {fullPath}",
                    BlueprintsImported = 0
                });
            }

            // Perform the import
            var seedingLogger = _loggerFactory.CreateLogger<BlueprintSeeding>();
            var seeding = new BlueprintSeeding(_dbContext, seedingLogger);
            await seeding.ImportBlueprintsFromJsonAsync(fullPath, cancellationToken);

            // Get count of imported blueprints
            var blueprintCount = _dbContext.Blueprints.Count();

            var result = new SeedingResult
            {
                Success = true,
                Message = "Blueprints imported successfully",
                BlueprintsImported = blueprintCount
            };

            _logger.LogInformation("Blueprint import completed. Total blueprints in database: {Count}", blueprintCount);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during blueprint import");
            return StatusCode(StatusCodes.Status500InternalServerError, new SeedingResult
            {
                Success = false,
                Message = $"Import failed: {ex.Message}",
                BlueprintsImported = 0
            });
        }
    }

    /// <summary>
    /// Clear all blueprints from database
    /// WARNING: This is destructive! Only available in development
    /// </summary>
    [HttpDelete("clear-blueprints")]
    [ProducesResponseType(typeof(SeedingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SeedingResult>> ClearBlueprints(CancellationToken cancellationToken = default)
    {
        // Only allow in development
        if (!_env.IsDevelopment())
        {
            return Forbid("Clear operations are only available in development environment");
        }

        try
        {
            _logger.LogWarning("Clearing all blueprints from database");

            var count = _dbContext.Blueprints.Count();
            _dbContext.Blueprints.RemoveRange(_dbContext.Blueprints);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cleared {Count} blueprints from database", count);

            return Ok(new SeedingResult
            {
                Success = true,
                Message = $"Deleted {count} blueprints",
                BlueprintsImported = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing blueprints");
            return StatusCode(StatusCodes.Status500InternalServerError, new SeedingResult
            {
                Success = false,
                Message = $"Clear failed: {ex.Message}",
                BlueprintsImported = 0
            });
        }
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(DatabaseStats), StatusCodes.Status200OK)]
    public ActionResult<DatabaseStats> GetStats()
    {
        return Ok(new DatabaseStats
        {
            GamesCount = _dbContext.Games.Count(),
            ExpansionsCount = _dbContext.Expansions.Count(),
            BlueprintsCount = _dbContext.Blueprints.Count(),
            InventoryItemsCount = _dbContext.InventoryItems.Count(),
            OrdersCount = _dbContext.Orders.Count()
        });
    }
}

/// <summary>
/// Result of seeding operation
/// </summary>
public class SeedingResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int BlueprintsImported { get; set; }
}

/// <summary>
/// Database statistics
/// </summary>
public class DatabaseStats
{
    public int GamesCount { get; set; }
    public int ExpansionsCount { get; set; }
    public int BlueprintsCount { get; set; }
    public int InventoryItemsCount { get; set; }
    public int OrdersCount { get; set; }
}
