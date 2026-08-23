using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// One-shot background service to populate BoxPrice on Expansions by fetching sealed product prices from Card Trader marketplace.
/// Uses the "sealed" category filter to find booster boxes, cases, starter decks, etc.
/// Takes the 10 lowest English prices per expansion, averages them, and saves to BoxPrice.
/// Runs once at startup when enabled, then completes and stops the application.
/// </summary>
public class SealedProductPriceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SealedProductPriceService> _logger;
    private readonly IConfiguration _configuration;

    public SealedProductPriceService(
        IServiceProvider serviceProvider,
        ILogger<SealedProductPriceService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool>("SyncSettings:PopulateSealedPricesOnStartup", false);

        if (!enabled)
        {
            _logger.LogInformation("SealedProductPriceService is disabled via configuration (SyncSettings:PopulateSealedPricesOnStartup). Skipping.");
            return;
        }

        _logger.LogInformation("========================================");
        _logger.LogInformation("Starting Sealed Product Price population service at {Time}", DateTime.Now);
        _logger.LogInformation("========================================");

        try
        {
            await RunPopulationAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sealed product price population cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sealed product price population");
        }
        finally
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("Sealed product price population service completed at {Time}", DateTime.Now);
            _logger.LogInformation("========================================");
        }

        // Stop the application after completion (since this is a one-shot service)
        Environment.Exit(0);
    }

    private async Task RunPopulationAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cardTraderApiService = scope.ServiceProvider.GetRequiredService<ICardTraderApiService>();

        // Step 1: Get all enabled expansions that don't have BoxPrice yet (or all if we want to refresh)
        var expansions = await dbContext.Expansions
            .AsNoTracking()
            .Include(e => e.Game)
            .Where(e => e.Game.IsEnabled)
            .OrderBy(e => e.Game.Name)
            .ThenBy(e => e.Name)
            .ToListAsync(stoppingToken);

        var totalCount = expansions.Count;
        _logger.LogInformation("Found {Count} enabled expansions to process", totalCount);

        if (totalCount == 0)
        {
            _logger.LogInformation("No enabled expansions found. Done.");
            return;
        }

        var processed = 0;
        var updated = 0;
        var skippedNoData = 0;
        var failed = 0;

        foreach (var expansion in expansions)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Cancellation requested, stopping population");
                break;
            }

            processed++;

            try
            {
                _logger.LogInformation("Processing expansion {Processed}/{Total}: {ExpansionName} (CT ID: {CTId})",
                    processed, totalCount, expansion.Name, expansion.CardTraderId);

                // Fetch marketplace products for this expansion
                var marketplaceProducts = await cardTraderApiService.GetMarketplaceProductsByExpansionAsync(expansion.CardTraderId, stoppingToken);

                if (marketplaceProducts == null || !marketplaceProducts.Any())
                {
                    _logger.LogInformation("No marketplace products found for expansion {ExpansionName}", expansion.Name);
                    skippedNoData++;
                    continue;
                }

                // Get all blueprints for this expansion to check sealed categories
                var blueprints = await dbContext.Blueprints
                    .AsNoTracking()
                    .Where(b => b.ExpansionId == expansion.Id)
                    .Select(b => new { b.CardTraderId, b.CategoryId, b.GameId })
                    .ToDictionaryAsync(b => b.CardTraderId, b => b, stoppingToken);

                // Group marketplace products by BlueprintId
                var productsByBlueprint = marketplaceProducts
                    .Where(p => p.BlueprintId > 0)
                    .GroupBy(p => p.BlueprintId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Filter marketplace products to find sealed products in English
                var sealedProductPrices = new List<int>(); // List of min prices in cents per sealed blueprint

                foreach (var kvp in productsByBlueprint)
                {
                    int blueprintId = kvp.Key;

                    if (!blueprints.TryGetValue(blueprintId, out var blueprintInfo))
                        continue;

                    // Check if this blueprint is a sealed product
                    if (!SealedCategoryIds.IsSealedCategory(blueprintInfo.GameId, blueprintInfo.CategoryId))
                        continue;

                    // Get products for this blueprint
                    var products = kvp.Value;

                    // Filter to English language only and get minimum price per blueprint
                    var englishProducts = products
                        .Where(p => p.Properties != null && p.Properties.Language != null &&
                                    p.Properties.Language.Equals("English", StringComparison.OrdinalIgnoreCase))
                        .Where(p => p.PriceCents > 0)
                        .ToList();

                    if (englishProducts.Any())
                    {
                        var minPrice = englishProducts.Min(p => p.PriceCents);
                        sealedProductPrices.Add(minPrice);
                    }
                }

                if (sealedProductPrices.Any())
                {
                    // Sort ascending and take the 10 lowest prices
                    sealedProductPrices.Sort();
                    var top10 = sealedProductPrices.Take(10).ToList();
                    var avgCents = (int)Math.Round(top10.Average());
                    var avgEuros = Math.Round(avgCents / 100m, 2);

                    // Update the expansion
                    var entity = await dbContext.Expansions.FirstOrDefaultAsync(e => e.Id == expansion.Id, stoppingToken);
                    if (entity != null)
                    {
                        entity.BoxPrice = avgEuros;
                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation("Updated BoxPrice for {ExpansionName}: €{BoxPrice:F2} (from {Count} sealed products, used top {TopCount})",
                            expansion.Name, avgEuros, sealedProductPrices.Count, top10.Count);
                        updated++;
                    }
                    else
                    {
                        _logger.LogWarning("Expansion {ExpansionId} not found in DB for update", expansion.Id);
                        failed++;
                    }
                }
                else
                {
                    _logger.LogInformation("No sealed products with English prices found for {ExpansionName}", expansion.Name);
                    skippedNoData++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Error processing expansion {ExpansionName} (ID: {ExpansionId})", expansion.Name, expansion.Id);
            }

            // Small delay to respect rate limiter between expansions
            if (processed < totalCount)
            {
                await Task.Delay(500, stoppingToken);
            }
        }

        _logger.LogInformation("========================================");
        _logger.LogInformation("Sealed product price population completed:");
        _logger.LogInformation("  Total expansions processed: {Total}", totalCount);
        _logger.LogInformation("  Successfully updated: {Updated}", updated);
        _logger.LogInformation("  Skipped (no data): {Skipped}", skippedNoData);
        _logger.LogInformation("  Failed: {Failed}", failed);
        _logger.LogInformation("========================================");
    }
}