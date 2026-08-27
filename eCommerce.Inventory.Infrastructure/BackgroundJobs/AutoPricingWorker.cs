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
/// Esecuzione notturna dell'autopricer.
///
/// Non tenta di riprezzare tutto il magazzino: con circa 19.000 blueprint distinti e un
/// limite di 20 richieste al minuto servirebbero 16 ore, ed è verosimilmente il motivo per
/// cui l'autopricer nativo lascia indietro delle carte. Ogni notte copre per intero le
/// carte sopra la soglia di valore e vi aggiunge una fetta a rotazione del bulk, scegliendo
/// quelle ferme da più tempo. La copertura risultante è completa dove conta e, soprattutto,
/// verificabile: ogni valutazione finisce a registro con il suo esito.
/// </summary>
public class AutoPricingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoPricingWorker> _logger;
    private readonly IConfiguration _configuration;

    public AutoPricingWorker(
        IServiceProvider serviceProvider,
        ILogger<AutoPricingWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("AutoPricing:Enabled", false))
        {
            _logger.LogInformation(
                "AutoPricingWorker disabilitato da configurazione (AutoPricing:Enabled). Nessuna esecuzione notturna.");
            return;
        }

        _logger.LogInformation("AutoPricingWorker avviato.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextRun = GetNextRunTime();
                var delay = nextRun - DateTime.Now;

                _logger.LogInformation("Prossima esecuzione autopricer prevista per {NextRun} (fra {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nell'AutoPricingWorker");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("AutoPricingWorker fermato.");
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pricingService = scope.ServiceProvider.GetRequiredService<AutoPricingService>();

        var profile = await context.PricingProfiles
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsActive, stoppingToken);

        if (profile == null)
        {
            _logger.LogWarning("Nessun profilo di pricing attivo: esecuzione notturna saltata");
            return;
        }

        var highValueThreshold = _configuration.GetValue("AutoPricing:HighValueThreshold", 1.00m);
        var bulkSliceSize = _configuration.GetValue("AutoPricing:BulkSliceSize", 2000);

        var blueprintIds = await pricingService.SelectBlueprintsForScheduledRunAsync(
            highValueThreshold, bulkSliceSize, stoppingToken);

        if (blueprintIds.Count == 0)
        {
            _logger.LogInformation("Nessun blueprint da valutare in questa esecuzione");
            return;
        }

        _logger.LogInformation("========================================");
        _logger.LogInformation(
            "Autopricer notturno: {Count} blueprint da valutare, profilo '{Profile}', dry-run={DryRun}",
            blueprintIds.Count, profile.Name, profile.DryRun);
        _logger.LogInformation("========================================");

        var run = await pricingService.RunAsync(
            blueprintIds,
            profile,
            PricingTrigger.Scheduled,
            forceDryRun: false,
            refreshPricesFirst: true,
            stoppingToken);

        _logger.LogInformation("========================================");
        _logger.LogInformation(
            "Autopricer notturno concluso: copertura {Coverage}%, applicati {Applied}, simulati {Simulated}, " +
            "invariati {NoChange}, saltati {Skipped}, falliti {Failed}, delta {Delta:0.00} €",
            run.CoveragePercent, run.AppliedCount, run.SimulatedCount,
            run.NoChangeCount, run.SkippedCount, run.FailedCount, run.TotalPriceDelta);
        _logger.LogInformation("========================================");
    }

    /// <summary>
    /// Prossima esecuzione all'orario configurato. Viene pianificata dopo la sync notturna
    /// dei prodotti, così l'autopricer lavora su un inventario già aggiornato.
    /// </summary>
    private DateTime GetNextRunTime()
    {
        var configuredTime = _configuration.GetValue<string>("AutoPricing:RunTime") ?? "03:30";

        if (!TimeSpan.TryParse(configuredTime, out var runTime))
        {
            _logger.LogWarning("Orario '{Configured}' non valido, uso 03:30", configuredTime);
            runTime = new TimeSpan(3, 30, 0);
        }

        var next = DateTime.Today.Add(runTime);
        if (next <= DateTime.Now) next = next.AddDays(1);

        return next;
    }
}
