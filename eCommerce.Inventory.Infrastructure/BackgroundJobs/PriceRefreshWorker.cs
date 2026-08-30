using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Consuma la coda dei blueprint da riprezzare fuori dall'esecuzione notturna.
///
/// Ci finiscono due casi. La vendita, perché è essa stessa un segnale di mercato: se una
/// carta viene venduta il suo prezzo potrebbe essere in salita, e rivalutarla subito evita
/// di lasciare a lungo una copia residua a un prezzo ormai basso. E la pubblicazione di una
/// nuova inserzione dalla maschera, dove il prezzo di partenza è messo a mano e va allineato
/// al mercato senza aspettare la notte.
///
/// Il worker non decide se quei casi siano attivi: la scelta sta a chi accoda, così l'unico
/// compito qui è svuotare la coda. Il riallineamento globale dei prezzi è disattivato:
/// costerebbe una chiamata di export per ogni singolo evento, mentre la valutazione
/// riguarda una sola carta.
/// </summary>
public class PriceRefreshWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPriceRefreshQueue _queue;
    private readonly ILogger<PriceRefreshWorker> _logger;

    public PriceRefreshWorker(
        IServiceProvider serviceProvider,
        IPriceRefreshQueue queue,
        ILogger<PriceRefreshWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PriceRefreshWorker avviato.");

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
                _logger.LogError(ex, "Errore nella rivalutazione fuori dall'esecuzione notturna");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("PriceRefreshWorker fermato.");
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
            request.Trigger,
            forceDryRun: false,
            refreshPricesFirst: false,
            stoppingToken);

        _logger.LogInformation(
            "Rivalutazione del blueprint {BlueprintId} conclusa: applicati {Applied}, simulati {Simulated}, " +
            "invariati {NoChange}, saltati {Skipped}",
            request.BlueprintId, run.AppliedCount, run.SimulatedCount, run.NoChangeCount, run.SkippedCount);
    }
}
