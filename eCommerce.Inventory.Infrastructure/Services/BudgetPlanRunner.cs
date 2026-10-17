using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Calcolo del pianificatore del budget in background (feature 007): la lettura delle offerte Card Trader dura
/// 1–2 minuti, troppo per una richiesta. Un calcolo alla volta, perché il limite di 20 richieste al minuto è
/// condiviso; un secondo avvio mentre il primo è in corso restituisce quello già partito.
///
/// I piani restano in memoria qualche ora, per ricalcolarli sulle stesse offerte: dopo, le offerte sono vecchie
/// e conviene rileggerle. Un riavvio del servizio li perde, e va bene così.
/// </summary>
public class BudgetPlanRunner
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BudgetPlanRunner> _logger;
    private readonly ConcurrentDictionary<string, Run> _runs = new();
    private readonly object _startLock = new();

    public BudgetPlanRunner(IServiceScopeFactory scopeFactory, ILogger<BudgetPlanRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public BudgetPlanStatus Start(BudgetPlanRequest request)
    {
        lock (_startLock)
        {
            Cleanup();
            var running = _runs.Values.FirstOrDefault(r => r.State == "running");
            if (running != null) return running.Status();

            var run = new Run(Guid.NewGuid().ToString("N")[..12], request);
            _runs[run.Id] = run;
            _ = Task.Run(() => ExecuteAsync(run));
            return run.Status();
        }
    }

    public BudgetPlanStatus? Get(string id) => _runs.TryGetValue(id, out var run) ? run.Status() : null;

    /// <summary>Ricalcola sulle offerte già lette, con righe tolte e spedizioni cambiate. Null se il piano non c'è più.</summary>
    public BudgetPlanStatus? Recompute(
        string id, BudgetShipping? shipping, IReadOnlyCollection<string> excluded, IReadOnlyCollection<string>? excludedStores)
    {
        if (!_runs.TryGetValue(id, out var run) || run.Data == null) return null;
        run.Shipping = shipping ?? run.Shipping;
        run.Result = BudgetPlanService.Compose(run.Data, run.Shipping!, excluded, excludedStores);
        return run.Status();
    }

    private async Task ExecuteAsync(Run run)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<BudgetPlanService>();
            var progress = new Progress<(int Done, int Total)>(p => { run.Done = p.Done; run.Total = p.Total; });
            run.Data = await service.CollectAsync(run.Request, progress, CancellationToken.None);
            run.Shipping = run.Request.Shipping ?? service.DefaultShipping;
            run.Result = BudgetPlanService.Compose(run.Data, run.Shipping, Array.Empty<string>(), run.Request.ExcludedStores);
            run.State = "done";
            _logger.LogInformation("Pianificatore del budget {Id}: {Budget} € di budget, spesi {Spent} €, {Baskets} carrelli",
                run.Id, run.Request.Budget, run.Result.Spent, run.Result.Baskets.Count);
        }
        catch (Exception ex)
        {
            run.State = "failed";
            run.Error = "Calcolo del piano non riuscito: " + ex.Message;
            _logger.LogError(ex, "Pianificatore del budget {Id} fallito", run.Id);
        }
    }

    private void Cleanup()
    {
        foreach (var old in _runs.Values.Where(r => r.State != "running" && DateTime.UtcNow - r.StartedAt > KeepFor).ToList())
            _runs.TryRemove(old.Id, out _);
    }

    private sealed class Run
    {
        public Run(string id, BudgetPlanRequest request)
        {
            Id = id;
            Request = request;
        }

        public string Id { get; }
        public BudgetPlanRequest Request { get; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public volatile string State = "running";
        public volatile int Done;
        public volatile int Total;
        public string? Error;
        public BudgetPlanData? Data;
        public BudgetShipping? Shipping;
        public BudgetPlanResult? Result;

        public BudgetPlanStatus Status() => new(Id, State, Done, Total, StartedAt, Error, Result);
    }
}

/// <param name="State">"running", "done" o "failed".</param>
/// <param name="Done">Prodotti di cui sono state lette le offerte Card Trader.</param>
public record BudgetPlanStatus(string Id, string State, int Done, int Total, DateTime StartedAt, string? Error, BudgetPlanResult? Result);
