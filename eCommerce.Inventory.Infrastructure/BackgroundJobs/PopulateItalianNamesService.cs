using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;
using eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// One-shot background service to populate ItalianName on all blueprints that have ScryfallId but missing ItalianName.
/// Uses MTGJSON (primary) and Scryfall (fallback) for Italian translations.
/// Runs once at startup when enabled, then completes and stops the application.
/// </summary>
public class PopulateItalianNamesService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PopulateItalianNamesService> _logger;
    private readonly IConfiguration _configuration;

    public PopulateItalianNamesService(
        IServiceProvider serviceProvider,
        ILogger<PopulateItalianNamesService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool>("SyncSettings:PopulateItalianNamesOnStartup", false);

        if (!enabled)
        {
            _logger.LogInformation("PopulateItalianNamesService is disabled via configuration (SyncSettings:PopulateItalianNamesOnStartup). Skipping.");
            return;
        }

        _logger.LogInformation("========================================");
        _logger.LogInformation("Starting ItalianName population service at {Time}", DateTime.Now);
        _logger.LogInformation("========================================");

        try
        {
            await RunPopulationAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ItalianName population cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ItalianName population");
        }
        finally
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("ItalianName population service completed at {Time}", DateTime.Now);
            _logger.LogInformation("========================================");
        }

        // Stop the application after completion (since this is a one-shot service)
        Environment.Exit(0);
    }

    private async Task RunPopulationAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scryfallClient = scope.ServiceProvider.GetRequiredService<IScryfallApiClient>();
        var mtgJsonLogger = scope.ServiceProvider.GetRequiredService<ILogger<MtgJsonClient>>();

        // Step 1: Download MTGJSON Italian names (primary source)
        _logger.LogInformation("Step 1: Downloading MTGJSON Italian names...");
        var mtgJsonClient = MtgJsonClientFactory.Create(mtgJsonLogger);
        var mtgJsonItalianNames = await mtgJsonClient.GetItalianNamesByScryfallCardIdAsync(stoppingToken);
        _logger.LogInformation("Downloaded {Count} Italian names from MTGJSON", mtgJsonItalianNames.Count);

        // Step 2: Get blueprints needing ItalianName
        var blueprintsNeedingItalianName = await dbContext.Blueprints
            .AsNoTracking()
            .Where(b => !string.IsNullOrWhiteSpace(b.ScryfallId) && string.IsNullOrWhiteSpace(b.ItalianName))
            .Select(b => new { b.Id, b.CardTraderId, b.ScryfallId, b.Name })
            .ToListAsync(stoppingToken);

        var totalCount = blueprintsNeedingItalianName.Count;
        _logger.LogInformation("Found {Count} blueprints with ScryfallId but missing ItalianName", totalCount);

        if (totalCount == 0)
        {
            _logger.LogInformation("No blueprints need ItalianName population. Done.");
            return;
        }

        var processed = 0;
        var updated = 0;
        var failed = 0;
        var skipped = 0;
        var fromMtgJson = 0;
        var fromScryfall = 0;
        var notFound = 0;

        const int batchSize = 500;
        var batches = blueprintsNeedingItalianName
            .Select((x, i) => new { x, i })
            .GroupBy(g => g.i / batchSize)
            .Select(g => g.Select(x => x.x).ToList())
            .ToList();

        _logger.LogInformation("Processing in {BatchCount} batches of {BatchSize}", batches.Count, batchSize);

        foreach (var batch in batches)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Cancellation requested, stopping population");
                break;
            }

            // Load entities for batch
            var batchIds = batch.Select(b => b.Id).ToList();
            var entities = await dbContext.Blueprints
                .Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b, stoppingToken);

            foreach (var bp in batch)
            {
                string? italianName = null;

                // Try MTGJSON first (by Scryfall Oracle ID)
                if (!string.IsNullOrWhiteSpace(bp.ScryfallId) &&
                    mtgJsonItalianNames.TryGetValue(bp.ScryfallId, out var mtgJsonName))
                {
                    italianName = mtgJsonName;
                    fromMtgJson++;
                }

                // Fallback to Scryfall API
                if (string.IsNullOrWhiteSpace(italianName) && !string.IsNullOrWhiteSpace(bp.ScryfallId))
                {
                    try
                    {
                        var card = await scryfallClient.GetCardByIdAsync(bp.ScryfallId, stoppingToken);

                        if (card?.Localized != null && card.Localized.TryGetValue("it", out var italianEntry))
                        {
                            italianName = italianEntry.Name;
                        }
                        else if (card != null && card.Lang.Equals("it", StringComparison.OrdinalIgnoreCase))
                        {
                            italianName = card.Name;
                        }

                        if (!string.IsNullOrWhiteSpace(italianName))
                        {
                            fromScryfall++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch Italian name from Scryfall for blueprint {BlueprintId} (ScryfallId: {ScryfallId})",
                            bp.CardTraderId, bp.ScryfallId);
                    }
                }

                if (!string.IsNullOrWhiteSpace(italianName) && entities.TryGetValue(bp.Id, out var entity))
                {
                    entity.ItalianName = italianName;
                    entity.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(italianName))
                        notFound++;
                    skipped++;
                }

                processed++;
            }

            // Save batch
            await dbContext.SaveChangesAsync(stoppingToken);

            _logger.LogInformation("Progress: {Processed}/{Total} processed, {Updated} updated ({FromMtgJson} MTGJSON, {FromScryfall} Scryfall), {NotFound} not found, {Failed} failed",
                processed, totalCount, updated, fromMtgJson, fromScryfall, notFound, failed);
        }

        _logger.LogInformation("========================================");
        _logger.LogInformation("ItalianName population completed:");
        _logger.LogInformation("  Total blueprints needing update: {Total}", totalCount);
        _logger.LogInformation("  Successfully updated: {Updated}", updated);
        _logger.LogInformation("    - From MTGJSON: {FromMtgJson}", fromMtgJson);
        _logger.LogInformation("    - From Scryfall: {FromScryfall}", fromScryfall);
        _logger.LogInformation("  Not found in any source: {NotFound}", notFound);
        _logger.LogInformation("  Failed: {Failed}", failed);
        _logger.LogInformation("========================================");
    }
}
