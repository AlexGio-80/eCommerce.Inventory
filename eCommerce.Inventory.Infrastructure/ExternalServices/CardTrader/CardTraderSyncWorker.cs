using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;

/// <summary>
/// Background service that periodically syncs data from Card Trader API
/// </summary>
public class CardTraderSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CardTraderSyncWorker> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(15); // Sync every 15 minutes

    public CardTraderSyncWorker(
        IServiceProvider serviceProvider,
        ILogger<CardTraderSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Card Trader sync worker started");

        // Give the application a moment to fully startup
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncCardTraderDataAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Card Trader sync. Retrying in {SyncInterval}", _syncInterval);
            }

            // Wait for the next sync interval
            await Task.Delay(_syncInterval, stoppingToken);
        }

        _logger.LogInformation("Card Trader sync worker stopped");
    }

    /// <summary>
    /// Perform a full sync of Card Trader data
    /// Orchestrates: API Fetch → DTO Mapping → Database Merge
    /// </summary>
    private async Task SyncCardTraderDataAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var cardTraderApiService = scope.ServiceProvider.GetRequiredService<ICardTraderApiService>();
            var mapper = scope.ServiceProvider.GetRequiredService<CardTraderDtoMapper>();
            var syncService = scope.ServiceProvider.GetRequiredService<InventorySyncService>();

            _logger.LogInformation("Starting Card Trader data sync");

            try
            {
                // ========== STEP 1: Sync Games & Expansions ==========
                _logger.LogInformation("Step 1: Syncing games from Card Trader API");
                var gameDtos = (await cardTraderApiService.SyncGamesAsync(cancellationToken))
                    .Cast<CardTraderGameDto>().ToList();
                if (gameDtos.Any())
                {
                    var games = mapper.MapGames(gameDtos);
                    _logger.LogInformation("Mapped {GameCount} games, syncing to database", games.Count);
                }

                _logger.LogInformation("Step 1: Syncing expansions from Card Trader API");
                var expansionDtos = (await cardTraderApiService.SyncExpansionsAsync(cancellationToken))
                    .Cast<CardTraderExpansionDto>().ToList();
                if (expansionDtos.Any())
                {
                    var expansions = mapper.MapExpansions(expansionDtos);
                    await syncService.SyncExpansionsAsync(expansionDtos, cancellationToken);
                }

                // ========== STEP 2: Fetch and Sync Products/Inventory ==========
                _logger.LogInformation("Step 2: Fetching my products from Card Trader API");
                var productDtos = (await cardTraderApiService.FetchMyProductsAsync(cancellationToken))
                    .Cast<CardTraderProductDto>().ToList();

                if (productDtos.Any())
                {
                    _logger.LogInformation("Fetched {ProductCount} products from Card Trader API", productDtos.Count);
                    var mappedItems = mapper.MapProductsToInventoryItems(productDtos);
                    _logger.LogInformation("Mapped {ItemCount} products to inventory items", mappedItems.Count);

                    // Merge with database: INSERT new, UPDATE existing
                    await syncService.SyncProductsAsync(productDtos, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("No products fetched from Card Trader API");
                }

                // ========== STEP 3: Fetch and Sync Orders ==========
                _logger.LogInformation("Step 3: Fetching orders from Card Trader API");
                var orderDtos = (await cardTraderApiService.GetOrdersAsync(null, null, cancellationToken))
                    .Cast<CardTraderOrderDto>().ToList();

                if (orderDtos.Any())
                {
                    _logger.LogInformation("Fetched {OrderCount} orders from Card Trader API", orderDtos.Count);

                    // Merge with database: INSERT new orders, UPDATE existing statuses
                    await syncService.SyncOrdersAsync(orderDtos, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("No new orders fetched from Card Trader API");
                }

                _logger.LogInformation("Card Trader data sync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Card Trader data sync");
                throw;
            }
        }
    }

    /// <summary>
    /// Manually trigger a sync (can be called via controller)
    /// </summary>
    public async Task ManualSyncAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Manual sync triggered for Card Trader data");
        await SyncCardTraderDataAsync(cancellationToken);
    }
}
