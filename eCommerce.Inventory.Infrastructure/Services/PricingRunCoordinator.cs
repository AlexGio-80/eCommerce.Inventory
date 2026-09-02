using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <inheritdoc cref="IPricingRunCoordinator"/>
public class PricingRunCoordinator : IPricingRunCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PricingRunCoordinator> _logger;

    private readonly object _gate = new();
    private ActiveRun? _active;

    public PricingRunCoordinator(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime,
        ILogger<PricingRunCoordinator> logger)
    {
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
        _logger = logger;
    }

    public PricingRunStatus? Current => _active?.ToStatus();

    public PricingRunStartResult Start(PricingRunStartRequest request)
    {
        lock (_gate)
        {
            if (_active != null)
            {
                _logger.LogWarning(
                    "Richiesta di esecuzione '{Requested}' rifiutata: '{Running}' è ancora in corso",
                    request.Description, _active.Request.Description);

                return new PricingRunStartResult(false, _active.ToStatus(), _active.Completion);
            }

            // Il token della richiesta HTTP non va bene: verrebbe annullato appena il
            // chiamante riceve la risposta, cioè subito. L'esecuzione si ferma solo se
            // la si annulla a mano o se l'applicazione sta chiudendo.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            var active = new ActiveRun(request, cts);
            _active = active;

            active.Completion = Task.Run(() => ExecuteAsync(active));

            _logger.LogInformation("Esecuzione autopricer avviata in background: {Description}", request.Description);

            return new PricingRunStartResult(true, active.ToStatus(), active.Completion);
        }
    }

    public bool RequestCancellation()
    {
        lock (_gate)
        {
            if (_active == null) return false;

            _logger.LogInformation("Richiesta interruzione dell'esecuzione '{Description}'", _active.Request.Description);
            _active.Phase = "Interruzione richiesta";
            _active.CancellationSource.Cancel();
            return true;
        }
    }

    /// <summary>
    /// Gestisce lo slot occupato attorno all'esecuzione vera. La separazione fra le due cose
    /// non è cosmetica: la liberazione dello slot dev'essere garantita a prescindere da come
    /// finisce il lavoro, ed è l'unica parte che si può verificare senza un magazzino vero.
    /// </summary>
    private async Task ExecuteAsync(ActiveRun active)
    {
        try
        {
            await RunCoreAsync(active, active.CancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Esecuzione '{Description}' interrotta", active.Request.Description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore nell'esecuzione '{Description}' dell'autopricer", active.Request.Description);
        }
        finally
        {
            // Lo slot va liberato in ogni caso: se restasse occupato dopo un errore,
            // nessuna esecuzione successiva potrebbe più partire fino al riavvio.
            lock (_gate)
            {
                if (ReferenceEquals(_active, active)) _active = null;
            }

            active.CancellationSource.Dispose();
        }
    }

    /// <summary>Il lavoro vero. Sovrascrivibile nei test per governarne la durata.</summary>
    protected virtual async Task RunCoreAsync(IPricingRunProgress active, CancellationToken token)
    {
        var request = active.Request;

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pricingService = scope.ServiceProvider.GetRequiredService<AutoPricingService>();

        var profile = await LoadProfileAsync(context, request.ProfileId, token);
        if (profile == null)
        {
            _logger.LogWarning("Nessun profilo di pricing disponibile: esecuzione '{Description}' saltata",
                request.Description);
            return;
        }

        var blueprintIds = request.BlueprintIds?.ToList();
        if (blueprintIds == null)
        {
            active.Phase = "Selezione delle carte";
            blueprintIds = await pricingService.SelectBlueprintsForScheduledRunAsync(
                request.HighValueThreshold, request.BulkSliceSize, token);
        }

        if (blueprintIds.Count == 0)
        {
            _logger.LogInformation("Nessun blueprint da valutare per '{Description}'", request.Description);
            return;
        }

        // L'allineamento dei prezzi locali a Card Trader è una chiamata di export che
        // può metterci parecchio: va segnalata, altrimenti a schermo sembra uno stallo.
        active.Phase = "Allineamento dei prezzi da Card Trader";

        _logger.LogInformation(
            "Autopricer ({Description}): {Count} blueprint da valutare, profilo '{Profile}', dry-run={DryRun}",
            request.Description, blueprintIds.Count, profile.Name, profile.DryRun && !request.ForceApply);

        if (request.ForceApply && profile.DryRun)
        {
            // Va detto a chiaro nel registro: è l'unico punto in cui i prezzi si muovono
            // davvero pur essendo il profilo in simulazione.
            _logger.LogWarning(
                "Applicazione richiesta esplicitamente su {Count} carte: i prezzi verranno scritti " +
                "su Card Trader nonostante il profilo '{Profile}' sia in dry-run",
                blueprintIds.Count, profile.Name);
        }

        var run = await pricingService.RunAsync(
            blueprintIds,
            profile,
            request.Trigger,
            forceDryRun: false,
            refreshPricesFirst: true,
            cancellationToken: token,
            onRunCreated: created =>
            {
                active.RunId = created.Id;
                active.Phase = "Valutazione delle carte";
            },
            forceApply: request.ForceApply);

        _logger.LogInformation(
            "Autopricer ({Description}) concluso: copertura {Coverage}%, applicati {Applied}, simulati {Simulated}, " +
            "invariati {NoChange}, saltati {Skipped}, falliti {Failed}, delta {Delta:0.00} €",
            request.Description, run.CoveragePercent, run.AppliedCount, run.SimulatedCount,
            run.NoChangeCount, run.SkippedCount, run.FailedCount, run.TotalPriceDelta);
    }

    private static Task<PricingProfile?> LoadProfileAsync(
        ApplicationDbContext context, int? profileId, CancellationToken cancellationToken)
    {
        var query = context.PricingProfiles.Include(p => p.Rules).AsQueryable();

        return profileId.HasValue
            ? query.FirstOrDefaultAsync(p => p.Id == profileId.Value, cancellationToken)
            : query.FirstOrDefaultAsync(p => p.IsActive, cancellationToken);
    }

    /// <summary>
    /// Quel che l'esecuzione può scrivere mentre lavora: a che punto è, e su quale riga di
    /// storico si legge l'avanzamento. Il resto dello stato è affare del coordinatore.
    /// </summary>
    protected interface IPricingRunProgress
    {
        PricingRunStartRequest Request { get; }
        string Phase { get; set; }
        int? RunId { get; set; }
    }

    private sealed class ActiveRun : IPricingRunProgress
    {
        public ActiveRun(PricingRunStartRequest request, CancellationTokenSource cancellationSource)
        {
            Request = request;
            CancellationSource = cancellationSource;
        }

        public PricingRunStartRequest Request { get; }
        public CancellationTokenSource CancellationSource { get; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;

        public Task Completion { get; set; } = Task.CompletedTask;

        // Scritti dal thread che esegue, letti da quello che serve la richiesta HTTP:
        // volatile perché fra i due non c'è nessun'altra sincronizzazione.
        private volatile string _phase = "Preparazione";
        private volatile int _runId;

        public string Phase
        {
            get => _phase;
            set => _phase = value;
        }

        /// <summary>Assente finché la riga di storico non esiste: 0 non è un identificativo valido.</summary>
        public int? RunId
        {
            get => _runId == 0 ? null : _runId;
            set => _runId = value ?? 0;
        }

        public PricingRunStatus ToStatus() => new(
            RunId,
            Request.Trigger,
            Request.Description,
            StartedAt,
            Phase,
            CancellationSource.IsCancellationRequested);
    }
}
