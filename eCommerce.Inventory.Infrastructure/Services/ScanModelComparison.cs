using System.Diagnostics;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Prova temporanea dei modelli più economici sul riconoscimento (feature 006, §6). Con
/// <c>Scan:CompareModels</c> configurato, ogni foto va anche a quei modelli, in background: il telefono
/// riceve sempre e solo la risposta del modello principale, senza attese in più.
///
/// Per ogni modello il log scrive la lettura, il primo candidato, il tempo e i token (la riga del
/// lettore). La carta aperta subito dopo dal telefono (<c>GET /api/scan/card/{id}</c>) è la risposta giusta
/// con cui confrontarli. Il confronto non entra nel limite giornaliero: costa una frazione del principale.
/// </summary>
public class ScanModelComparison
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly ICardImageReader _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScanModelComparison> _logger;

    public ScanModelComparison(
        ICardImageReader reader,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ScanModelComparison> logger)
    {
        _reader = reader;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Modelli da confrontare, escluso il principale; vuoto = prova spenta.</summary>
    public IReadOnlyList<string> Models
    {
        get
        {
            var main = _configuration["Scan:Model"] ?? AnthropicClientProvider.DefaultModel;
            return _configuration.GetSection("Scan:CompareModels").Get<string[]>()?
                       .Select(m => m.Trim())
                       .Where(m => m.Length > 0 && !m.Equals(main, StringComparison.OrdinalIgnoreCase))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList()
                   ?? new List<string>();
        }
    }

    /// <summary>Avvia le letture di confronto e torna subito; un errore finisce solo nel log.</summary>
    public void Start(string scanId, byte[] image, string mediaType)
    {
        foreach (var model in Models)
        {
            _ = Task.Run(() => CompareAsync(scanId, model, image, mediaType));
        }
    }

    /// <summary>La riga del modello principale, con lo stesso formato, per leggere il confronto a colpo d'occhio.</summary>
    public void LogMain(string scanId, string model, CardScanResultDto result, long elapsedMs)
    {
        if (Models.Count == 0) return;
        Log(scanId, model, result.Reading, result.Candidates.FirstOrDefault(), elapsedMs);
    }

    private async Task CompareAsync(string scanId, string model, byte[] image, string mediaType)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var watch = Stopwatch.StartNew();
        try
        {
            var reading = await _reader.ReadAsync(image, mediaType, model, timeout.Token);
            var elapsed = watch.ElapsedMilliseconds;

            // Servizio con scope suo: la richiesta del telefono è già finita, e il suo DbContext con lei.
            using var scope = _scopeFactory.CreateScope();
            var scan = scope.ServiceProvider.GetRequiredService<CardScanService>();
            var candidates = await scan.FindCandidatesAsync(reading, timeout.Token);

            Log(scanId, model, reading, candidates.FirstOrDefault(), elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Confronto modelli [{ScanId}] {Model}: lettura non riuscita dopo {ElapsedMs} ms",
                scanId, model, watch.ElapsedMilliseconds);
        }
    }

    private void Log(string scanId, string model, CardReading r, ScanCandidateDto? first, long elapsedMs)
    {
        _logger.LogInformation(
            "Confronto modelli [{ScanId}] {Model}: letto \"{Name}\" set {SetCode} n. {Number} {Language} foil {IsFoil} sicurezza {Confidence}; " +
            "primo candidato {BlueprintId} \"{CandidateName}\" {ExpansionCode} n. {CandidateNumber} (esatto {Exact}); {ElapsedMs} ms",
            scanId, model, r.Name, r.SetCode, r.CollectorNumber, r.Language, r.IsFoil, r.Confidence,
            first?.BlueprintId, first?.Name, first?.ExpansionCode, first?.CollectorNumber, first?.ExactPrint, elapsedMs);
    }
}
