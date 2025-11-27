using Microsoft.AspNetCore.Mvc;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using Microsoft.AspNetCore.RateLimiting;

namespace eCommerce.Inventory.Api.Controllers.CardTrader;

[ApiController]
[Route("api/cardtrader/sync")]
[EnableRateLimiting("cardtrader-sync")]
public class CardTraderSyncController : ControllerBase
{
    private readonly ICardTraderApiService _cardTraderApiService;
    private readonly CardTraderSyncOrchestrator _syncOrchestrator;
    private readonly ILogger<CardTraderSyncController> _logger;

    public CardTraderSyncController(
        ICardTraderApiService cardTraderApiService,
        CardTraderSyncOrchestrator syncOrchestrator,
        ILogger<CardTraderSyncController> logger)
    {
        _cardTraderApiService = cardTraderApiService;
        _syncOrchestrator = syncOrchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Perform selective synchronization of Card Trader data
    /// Allows clients to choose which entities to sync (Games, Categories, Expansions, Blueprints, Properties)
    /// </summary>
    /// <param name="request">Sync request with entity selection flags</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync response with statistics (added, updated, failed)</returns>
    [HttpPost]
    public async Task<IActionResult> Sync(
        [FromBody] SyncRequestDto request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received sync request: Games={Games}, Categories={Categories}, Expansions={Expansions}, Blueprints={Blueprints}, Properties={Properties}",
            request.SyncGames, request.SyncCategories, request.SyncExpansions, request.SyncBlueprints, request.SyncProperties);

        try
        {
            var response = await _syncOrchestrator.SyncAsync(request, cancellationToken);

            if (response.ErrorMessage != null && request.IsEmpty)
            {
                return BadRequest(new { message = response.ErrorMessage, data = response });
            }

            _logger.LogInformation("Sync completed successfully. Added: {Added}, Updated: {Updated}, Failed: {Failed}",
                response.Added, response.Updated, response.Failed);

            return Ok(new
            {
                message = "Synchronization completed successfully",
                data = response
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during selective sync");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error during synchronization", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually sync all games from Card Trader API
    /// </summary>
    [HttpPost("games")]
    public async Task<IActionResult> SyncGames(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting manual sync of games from Card Trader");

        try
        {
            var games = await _cardTraderApiService.SyncGamesAsync(cancellationToken);
            _logger.LogInformation("Synced {GameCount} games from Card Trader", games.Count());
            return Ok(new { message = "Games synced successfully", count = games.Count() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing games from Card Trader");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error syncing games", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually sync all expansions from Card Trader API
    /// </summary>
    [HttpPost("expansions")]
    public async Task<IActionResult> SyncExpansions(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting manual sync of expansions from Card Trader");

        try
        {
            var expansions = await _cardTraderApiService.SyncExpansionsAsync(cancellationToken);
            _logger.LogInformation("Synced {ExpansionCount} expansions from Card Trader", expansions.Count());
            return Ok(new { message = "Expansions synced successfully", count = expansions.Count() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing expansions from Card Trader");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error syncing expansions", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually sync blueprints for a specific expansion from Card Trader API
    /// </summary>
    [HttpPost("blueprints")]
    public async Task<IActionResult> SyncBlueprints(
        [FromQuery] int expansionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting manual sync of blueprints for expansion {ExpansionId} from Card Trader", expansionId);

        if (expansionId <= 0)
        {
            return BadRequest(new { message = "expansionId must be a positive integer" });
        }

        try
        {
            var blueprints = await _cardTraderApiService.SyncBlueprintsForExpansionAsync(expansionId, cancellationToken);
            _logger.LogInformation("Synced {BlueprintCount} blueprints for expansion {ExpansionId} from Card Trader",
                blueprints.Count(), expansionId);
            return Ok(new { message = "Blueprints synced successfully", count = blueprints.Count() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing blueprints for expansion {ExpansionId} from Card Trader", expansionId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error syncing blueprints", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually fetch and sync all products from Card Trader
    /// </summary>
    [HttpPost("products")]
    public async Task<IActionResult> SyncProducts(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting manual sync of products from Card Trader");

        try
        {
            var products = await _cardTraderApiService.FetchMyProductsAsync(cancellationToken);
            _logger.LogInformation("Synced {ProductCount} products from Card Trader", products.Count());
            return Ok(new { message = "Products synced successfully", count = products.Count() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing products from Card Trader");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error syncing products", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually fetch and sync all orders from Card Trader
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> SyncOrders(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting manual sync of orders from Card Trader");

        try
        {
            var orders = await _cardTraderApiService.GetOrdersAsync(null, null, cancellationToken);
            _logger.LogInformation("Synced {OrderCount} orders from Card Trader", orders.Count());
            return Ok(new { message = "Orders synced successfully", count = orders.Count() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing orders from Card Trader");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error syncing orders", error = ex.Message });
        }
    }

    /// <summary>
    /// Manually trigger a full synchronization (games, expansions, blueprints, products, orders)
    /// </summary>
    [HttpPost("full")]
    public async Task<IActionResult> FullSync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting full sync of Card Trader data");

        try
        {
            // Execute syncs in order
            var games = await _cardTraderApiService.SyncGamesAsync(cancellationToken);
            var expansions = await _cardTraderApiService.SyncExpansionsAsync(cancellationToken);
            var products = await _cardTraderApiService.FetchMyProductsAsync(cancellationToken);
            var orders = await _cardTraderApiService.GetOrdersAsync(null, null, cancellationToken);

            _logger.LogInformation(
                "Full sync completed. Games: {GameCount}, Expansions: {ExpansionCount}, Products: {ProductCount}, Orders: {OrderCount}",
                games.Count(), expansions.Count(), products.Count(), orders.Count());

            return Ok(new
            {
                message = "Full sync completed successfully",
                games = games.Count(),
                expansions = expansions.Count(),
                products = products.Count(),
                orders = orders.Count()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during full sync of Card Trader data");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Error during full sync", error = ex.Message });
        }
    }
}
