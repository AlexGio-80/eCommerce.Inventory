using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Consuma la coda dei blueprint da riprezzare dopo una vendita.
///
/// L'idea è che la vendita stessa è un segnale di mercato: se una carta viene venduta il
/// suo prezzo potrebbe essere in salita, e rivalutarla subito evita di lasciare a lungo
/// una copia residua a un prezzo ormai basso. Il riallineamento globale dei prezzi qui è
/// disattivato: costerebbe una chiamata di export per ogni singola vendita, mentre la
/// valutazione riguarda una sola carta.
/// </summary>
public class OrderTriggeredPricingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPriceRefreshQueue _queue;
    private readonly ILogger<OrderTriggeredPricingWorker> _logger;
    private readonly IConfiguration _configuration;

    public OrderTriggeredPricingWorker(
        IServiceProvider serviceProvider,
        IPriceRefreshQueue queue,
        ILogger<OrderTriggeredPricingWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("AutoPricing:RepriceOnOrder", false))
        {
            _logger.LogInformation(
                "Reprice alla vendita disabilitato da configurazione (AutoPricing:RepriceOnOrder).");
            return;
        }

        _logger.LogInformation("OrderTriggeredPricingWorker avviato.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = await _queue.DequeueAsync(stoppingToken);
                await ProcessAsync(request, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Una singola carta fallita non deve fermare il consumo della coda.
                _logger.LogError(ex, "Errore nella rivalutazione innescata da vendita");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("OrderTriggeredPricingWorker fermato.");
    }

    private async Task ProcessAsync(PriceRefreshRequest request, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pricingService = scope.ServiceProvider.GetRequiredService<AutoPricingService>();

        var profile = await context.PricingProfiles
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsActive, stoppingToken);

        if (profile == null)
        {
            _logger.LogWarning("Nessun profilo di pricing attivo: rivalutazione del blueprint {BlueprintId} saltata",
                request.BlueprintId);
            return;
        }

        _logger.LogInformation(
            "Rivalutazione immediata del blueprint {BlueprintId} ({Reason})",
            request.BlueprintId, request.Reason);

        var run = await pricingService.RunAsync(
            new[] { request.BlueprintId },
            profile,
            PricingTrigger.OrderReceived,
            forceDryRun: false,
            refreshPricesFirst: false,
            stoppingToken);

        _logger.LogInformation(
            "Rivalutazione del blueprint {BlueprintId} conclusa: applicati {Applied}, simulati {Simulated}, " +
            "invariati {NoChange}, saltati {Skipped}",
            request.BlueprintId, run.AppliedCount, run.SimulatedCount, run.NoChangeCount, run.SkippedCount);
    }
}
