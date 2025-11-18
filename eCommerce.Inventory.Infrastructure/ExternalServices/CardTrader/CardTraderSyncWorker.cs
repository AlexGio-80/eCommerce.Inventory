using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using eCommerce.Inventory.Application.Interfaces;

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
    /// </summary>
    private async Task SyncCardTraderDataAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var cardTraderApiService = scope.ServiceProvider.GetRequiredService<ICardTraderApiService>();
            var inventoryRepository = scope.ServiceProvider.GetRequiredService<IInventoryItemRepository>();
            var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            _logger.LogInformation("Starting Card Trader data sync");

            try
            {
                // Step 1: Sync games and expansions (less frequent, but included here)
                _logger.LogInformation("Syncing games from Card Trader");
                await cardTraderApiService.SyncGamesAsync(cancellationToken);

                _logger.LogInformation("Syncing expansions from Card Trader");
                await cardTraderApiService.SyncExpansionsAsync(cancellationToken);

                // Step 2: Fetch my products and sync with database
                _logger.LogInformation("Syncing products from Card Trader");
                var products = await cardTraderApiService.FetchMyProductsAsync(cancellationToken);
                // TODO: Merge products with database (create new, update existing)

                // Step 3: Fetch orders and sync with database
                _logger.LogInformation("Syncing orders from Card Trader");
                var orders = await cardTraderApiService.FetchNewOrdersAsync(cancellationToken);
                // TODO: Process new orders, update order items, adjust inventory

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
