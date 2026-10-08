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
/// Lettura del negozio Secret Lair agli orari <c>SecretLair:Monitor:RunTimes</c> (predefiniti 08:00,
/// 14:00, 20:00; attivabile via <c>SecretLair:Monitor:Enabled</c>). All'avvio legge solo se l'ultima
/// lettura riuscita ha più di <see cref="StartupMinAge"/>: i riavvii per le pubblicazioni non devono
/// moltiplicare le richieste a un sito di terzi.
/// </summary>
public class SecretLairMonitorWorker : BackgroundService
{
    private static readonly TimeSpan StartupMinAge = TimeSpan.FromHours(4);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecretLairMonitorWorker> _logger;
    private readonly IConfiguration _configuration;

    public SecretLairMonitorWorker(IServiceScopeFactory scopeFactory, ILogger<SecretLairMonitorWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("SecretLair:Monitor:Enabled", false))
        {
            _logger.LogInformation("SecretLairMonitorWorker disabilitato da configurazione (SecretLair:Monitor:Enabled).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            if (await LastSuccessAgeAsync(stoppingToken) > StartupMinAge) await RunAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var next = NextRunTime(DateTime.Now, RunTimes());
                _logger.LogInformation("Prossima lettura del negozio Secret Lair alle {Next}", next);
                await Task.Delay(next - DateTime.Now, stoppingToken);
                await RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SecretLairShopMonitorService>().RunAsync(stoppingToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Lettura del negozio Secret Lair saltata: {Message}", ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Errore nel SecretLairMonitorWorker");
        }
    }

    private async Task<TimeSpan> LastSuccessAgeAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var last = await db.SecretLairShopRuns
            .Where(r => r.Outcome == SecretLairShopRunOutcome.Succeeded)
            .MaxAsync(r => (DateTime?)r.StartedAt, stoppingToken);
        return last is null ? TimeSpan.MaxValue : DateTime.UtcNow - last.Value;
    }

    private List<TimeSpan> RunTimes()
    {
        var configured = _configuration.GetSection("SecretLair:Monitor:RunTimes").Get<string[]>() ?? new[] { "08:00", "14:00", "20:00" };
        var times = configured.Select(t => TimeSpan.TryParse(t, out var ts) ? ts : (TimeSpan?)null).Where(t => t.HasValue).Select(t => t!.Value).ToList();
        return times.Count > 0 ? times : new List<TimeSpan> { new(8, 0, 0) };
    }

    /// <summary>Il primo degli orari configurati dopo <paramref name="now"/>, oggi o domani.</summary>
    public static DateTime NextRunTime(DateTime now, IReadOnlyCollection<TimeSpan> times) =>
        times.Select(t => now.Date.Add(t)).Where(t => t > now).DefaultIfEmpty(now.Date.AddDays(1).Add(times.Min())).Min();
}
