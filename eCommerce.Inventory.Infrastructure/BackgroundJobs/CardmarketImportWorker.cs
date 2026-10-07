using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Import giornaliero del listino Cardmarket e del catalogo sigillati MTGJSON (attivabile via
/// <c>CardmarketImport:Enabled</c>).
///
/// Cardmarket rigenera il listino intorno all'01:00 italiana; l'import parte all'orario
/// <c>CardmarketImport:RunTime</c> (default 07:00). Gira anche all'avvio del servizio: dopo una
/// pubblicazione o un riavvio il giorno non va perso, e se il listino è già stato importato
/// l'import si ferma subito senza scaricare nulla.
/// </summary>
public class CardmarketImportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CardmarketImportWorker> _logger;
    private readonly IConfiguration _configuration;

    public CardmarketImportWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<CardmarketImportWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("CardmarketImport:Enabled", false))
        {
            _logger.LogInformation(
                "CardmarketImportWorker disabilitato da configurazione (CardmarketImport:Enabled). Nessun import del listino.");
            return;
        }

        _logger.LogInformation("CardmarketImportWorker avviato.");

        try
        {
            // Lascia finire l'avvio (migration, altri worker) prima di scaricare decine di MB.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            await RunAsync(CardmarketImportTrigger.Startup, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var nextRun = GetNextRunTime();
                _logger.LogInformation("Prossimo import listino Cardmarket previsto per {NextRun}", nextRun);

                await Task.Delay(nextRun - DateTime.Now, stoppingToken);
                await RunAsync(CardmarketImportTrigger.Scheduled, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }

        _logger.LogInformation("CardmarketImportWorker fermato.");
    }

    private async Task RunAsync(CardmarketImportTrigger trigger, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var importer = scope.ServiceProvider.GetRequiredService<CardmarketPriceImportService>();
            await importer.ImportAsync(trigger, cancellationToken: stoppingToken);
        }
        catch (CardmarketImportAlreadyRunningException)
        {
            _logger.LogWarning("Import listino Cardmarket saltato: ne è già in corso uno lanciato a mano");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // L'esito di un import fallito è già a registro; qui arriva solo ciò che è successo
            // prima di poterlo scrivere (es. database irraggiungibile). Il giorno dopo si riprova.
            _logger.LogError(ex, "Errore nel CardmarketImportWorker");
        }

        // Catalogo dei sigillati da MTGJSON, nello stesso giro ma indipendente: un errore qui non
        // tocca lo storico prezzi, e viceversa. Il contenuto dei prodotti cambia di rado, ma i
        // prodotti nuovi delle uscite in preordine compaiono nel giro di pochi giorni.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<SealedCatalogImportService>();
            await catalog.ImportAsync(stoppingToken);

            // Composizione delle buste delle uscite recenti e in arrivo: per quelle in preordine
            // compare su MTGJSON intorno all'uscita, e così arriva senza doverla chiedere a mano.
            var details = scope.ServiceProvider.GetRequiredService<MtgjsonSetDetailImportService>();
            await details.ImportRecentAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import del catalogo sigillati o dei dati delle buste MTGJSON fallito");
        }
    }

    private DateTime GetNextRunTime()
    {
        var configuredTime = _configuration.GetValue<string>("CardmarketImport:RunTime") ?? "07:00";

        if (!TimeSpan.TryParse(configuredTime, out var runTime))
        {
            _logger.LogWarning("Orario '{Configured}' non valido, uso 07:00", configuredTime);
            runTime = new TimeSpan(7, 0, 0);
        }

        var next = DateTime.Today.Add(runTime);
        if (next <= DateTime.Now) next = next.AddDays(1);

        return next;
    }
}
