using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Background service that runs scheduled product synchronization
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
        _logger.LogInformation("Scheduled Product Sync Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextRunTime = GetNextRunTime();
                var delay = nextRunTime - DateTime.Now;

                _logger.LogInformation("Next product sync scheduled for {NextRunTime} (in {Delay})", nextRunTime, delay);

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
                _logger.LogError(ex, "Error in Scheduled Product Sync Worker");
                // Wait a bit before retrying to avoid tight loop in case of error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Scheduled Product Sync Worker stopped.");
    }

    private async Task RunSyncAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<CardTraderSyncOrchestrator>();

        _logger.LogInformation("Starting scheduled product synchronization...");

        var request = new SyncRequestDto
        {
            SyncInventory = true
            // We only sync inventory in the scheduled job, assuming other metadata is relatively static or synced manually
        };

        var result = await orchestrator.SyncAsync(request, stoppingToken);

        if (result.ErrorMessage != null)
        {
            _logger.LogError("Scheduled product sync failed: {ErrorMessage}", result.ErrorMessage);
        }
        else
        {
            _logger.LogInformation("Scheduled product sync completed. Added: {Added}, Updated: {Updated}, Failed: {Failed}, Skipped: {Skipped}, Deleted: {Deleted}",
                result.Inventory.Added, result.Inventory.Updated, result.Inventory.Failed, result.Inventory.Skipped,
                // Note: Deleted count is not explicitly in SyncEntityResultDto but logged by Orchestrator. 
                // We can add it to DTO if needed, but for now logging is enough.
                "N/A");
        }
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
