using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Background service that runs scheduled full synchronization of all Card Trader entities
/// </summary>
public class ScheduledProductSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledProductSyncWorker> _logger;
    private readonly IConfiguration _configuration;

    public ScheduledProductSyncWorker(
        IServiceProvider serviceProvider,
        ILogger<ScheduledProductSyncWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled Full Sync Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextRunTime = GetNextRunTime();
                var delay = nextRunTime - DateTime.Now;

                _logger.LogInformation("Next FULL sync scheduled for {NextRunTime} (in {Delay})", nextRunTime, delay);

                await Task.Delay(delay, stoppingToken);

                await RunSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Scheduled Full Sync Worker");
                // Wait a bit before retrying to avoid tight loop in case of error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Scheduled Full Sync Worker stopped.");
    }

    private async Task RunSyncAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<CardTraderSyncOrchestrator>();

        _logger.LogInformation("========================================");
        _logger.LogInformation("Starting FULL scheduled synchronization at {Time}", DateTime.Now);
        _logger.LogInformation("========================================");

        var request = new SyncRequestDto
        {
            SyncGames = true,
            SyncExpansions = true,
            SyncBlueprints = true,
            SyncInventory = true,
            SyncOrders = true
        };

        var result = await orchestrator.SyncAsync(request, stoppingToken);

        _logger.LogInformation("========================================");
        _logger.LogInformation("Scheduled sync completed at {Time}", DateTime.Now);

        if (result.ErrorMessage != null)
        {
            _logger.LogError("Sync FAILED with error: {ErrorMessage}", result.ErrorMessage);
        }
        else
        {
            _logger.LogInformation("Sync SUCCESSFUL. Summary:");
            _logger.LogInformation("  - Games: Added={Added}, Updated={Updated}, Failed={Failed}, Skipped={Skipped}",
                result.Games.Added, result.Games.Updated, result.Games.Failed, result.Games.Skipped);
            _logger.LogInformation("  - Expansions: Added={Added}, Updated={Updated}, Failed={Failed}, Skipped={Skipped}",
                result.Expansions.Added, result.Expansions.Updated, result.Expansions.Failed, result.Expansions.Skipped);
            _logger.LogInformation("  - Blueprints: Added={Added}, Updated={Updated}, Failed={Failed}, Skipped={Skipped}",
                result.Blueprints.Added, result.Blueprints.Updated, result.Blueprints.Failed, result.Blueprints.Skipped);
            _logger.LogInformation("  - Inventory: Added={Added}, Updated={Updated}, Failed={Failed}, Skipped={Skipped}",
                result.Inventory.Added, result.Inventory.Updated, result.Inventory.Failed, result.Inventory.Skipped);
            _logger.LogInformation("  - Orders: Added={Added}, Updated={Updated}, Failed={Failed}, Skipped={Skipped}",
                result.Orders.Added, result.Orders.Updated, result.Orders.Failed, result.Orders.Skipped);
        }

        _logger.LogInformation("========================================");
    }

    private DateTime GetNextRunTime()
    {
        var scheduleTimeStr = _configuration["SyncSettings:ProductSyncTime"] ?? "03:00";
        if (!TimeSpan.TryParse(scheduleTimeStr, out var scheduleTime))
        {
            _logger.LogWarning("Invalid sync time format in configuration: {TimeStr}. Using default 03:00.", scheduleTimeStr);
            scheduleTime = new TimeSpan(3, 0, 0);
        }

        var now = DateTime.Now;
        var nextRun = now.Date.Add(scheduleTime);

        if (now >= nextRun)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun;
    }
}
