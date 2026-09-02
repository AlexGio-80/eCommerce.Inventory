using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.Extensions.Configuration;
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
    private readonly IPricingRunCoordinator _coordinator;
    private readonly ILogger<AutoPricingWorker> _logger;
    private readonly IConfiguration _configuration;

    public AutoPricingWorker(
        IPricingRunCoordinator coordinator,
        ILogger<AutoPricingWorker> logger,
        IConfiguration configuration)
    {
        _coordinator = coordinator;
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

    /// <summary>
    /// La notturna non esegue da sé: passa dal coordinatore come qualsiasi altra esecuzione.
    /// Così se una manuale lanciata la sera prima è ancora in corso all'orario previsto, la
    /// notturna se ne accorge invece di sovrapporsi e spartirsi il limite di richieste.
    /// </summary>
    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var highValueThreshold = _configuration.GetValue("AutoPricing:HighValueThreshold", 1.00m);
        var bulkSliceSize = _configuration.GetValue("AutoPricing:BulkSliceSize", 2000);

        _logger.LogInformation("========================================");
        _logger.LogInformation("Autopricer notturno: avvio");
        _logger.LogInformation("========================================");

        var result = _coordinator.Start(new PricingRunStartRequest(
            PricingTrigger.Scheduled,
            "Esecuzione notturna",
            HighValueThreshold: highValueThreshold,
            BulkSliceSize: bulkSliceSize));

        if (!result.Started)
        {
            _logger.LogWarning(
                "Esecuzione notturna saltata: '{Running}' è iniziata alle {StartedAt} ed è ancora in corso",
                result.Status.Description, result.Status.StartedAt);
            return;
        }

        // Attendere il completamento tiene il ciclo del worker allineato all'esecuzione
        // reale: senza attesa ripianificherebbe subito la notte successiva mentre questa
        // sta ancora girando. Il riepilogo finale lo scrive il coordinatore.
        await result.Completion.WaitAsync(stoppingToken);
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
