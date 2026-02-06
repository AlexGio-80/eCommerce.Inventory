using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System;

namespace eCommerce.Inventory.Infrastructure.Services;

public class ExpansionAnalyticsService : IExpansionAnalyticsService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICardTraderApiService _cardTraderApiService;
    private readonly ILogger<ExpansionAnalyticsService> _logger;

    public ExpansionAnalyticsService(
        IApplicationDbContext dbContext,
        ICardTraderApiService cardTraderApiService,
        ILogger<ExpansionAnalyticsService> logger)
    {
        _dbContext = dbContext;
        _cardTraderApiService = cardTraderApiService;
        _logger = logger;
    }

    public async Task<ExpansionAnalysisResult> AnalyzeExpansionValueAsync(int expansionId, CancellationToken cancellationToken = default)
    {
        var expansion = await _dbContext.Expansions
            .Include(e => e.Blueprints)
            .FirstOrDefaultAsync(e => e.Id == expansionId, cancellationToken);

        if (expansion == null)
        {
            _logger.LogWarning("Expansion {ExpansionId} not found for analysis", expansionId);
            throw new ArgumentException($"Espansione con ID {expansionId} non trovata.");
        }

        if (expansion.Blueprints.Count == 0)
        {
            _logger.LogWarning("No blueprints found for expansion {ExpansionName} ({ExpansionId})", expansion.Name, expansionId);
            throw new InvalidOperationException($"Nessun blueprint trovato per l'espansione '{expansion.Name}'. Eseguire prima la sincronizzazione dei blueprint.");
        }

        _logger.LogInformation("Starting value analysis for expansion {ExpansionName} ({ExpansionId}) via expansion_id filter",
            expansion.Name, expansionId);

        var blueprints = expansion.Blueprints.ToList();
        var prices = new Dictionary<int, int>(); // blueprintId -> minPriceCents

        try
        {
            var marketplaceProducts = await _cardTraderApiService.GetMarketplaceProductsByExpansionAsync(expansion.CardTraderId, cancellationToken);

            // Filter: only include playable cards (those having "tournament_legal" in properties_hash)
            // This excludes boxes, fat packs, and other non-card items.
            var cardOnlyProducts = marketplaceProducts
                .Where(p => p.PropertiesHash != null && p.PropertiesHash.ContainsKey("tournament_legal"))
                .ToList();

            _logger.LogInformation("Filtered {Total} products down to {CardsOnly} playable cards for expansion {ExpansionId}",
                marketplaceProducts.Count(), cardOnlyProducts.Count, expansionId);

            // --- RARITY ANALYSIS START ---
            // Helper to get rarity from properties_hash
            string GetRarity(Dictionary<string, object> props)
            {
                if (props != null && props.TryGetValue("mtg_rarity", out var rarityObj))
                {
                    return rarityObj?.ToString()?.ToLowerInvariant() ?? "";
                }
                return "";
            }

            var productsByRarity = cardOnlyProducts.GroupBy(p => GetRarity(p.PropertiesHash));

            // Helper to calculate average for a specific rarity group
            decimal CalculateRarityAverage(string targetRarity)
            {
                var rarityGroup = productsByRarity.FirstOrDefault(g => g.Key.Equals(targetRarity, StringComparison.OrdinalIgnoreCase));
                if (rarityGroup == null || !rarityGroup.Any()) return 0;

                // We need to calculate the average of the MINIMUM price of each card in that rarity
                var blueprintsInRarity = rarityGroup.GroupBy(p => p.BlueprintId);
                var minPrices = new List<decimal>();

                foreach (var bpGroup in blueprintsInRarity)
                {
                    var minPrice = bpGroup.Where(p => p.PriceCents > 0).Min(p => (int?)p.PriceCents);
                    if (minPrice.HasValue)
                    {
                        minPrices.Add((decimal)minPrice.Value / 100m);
                    }
                }

                return minPrices.Any() ? minPrices.Average() : 0;
            }

            expansion.AvgValueCommon = CalculateRarityAverage("common");
            expansion.AvgValueUncommon = CalculateRarityAverage("uncommon");
            expansion.AvgValueRare = CalculateRarityAverage("rare");
            expansion.AvgValueMythic = CalculateRarityAverage("mythic");

            _logger.LogInformation("Rarity Averages for {Expansion}: C={C:F2}, U={U:F2}, R={R:F2}, M={M:F2}",
                expansion.Name, expansion.AvgValueCommon, expansion.AvgValueUncommon, expansion.AvgValueRare, expansion.AvgValueMythic);
            // --- RARITY ANALYSIS END ---

            // Group by blueprint and calculate min
            var stats = cardOnlyProducts.GroupBy(p => p.BlueprintId);

            foreach (var group in stats)
            {
                var minPrice = group.Where(p => p.PriceCents > 0).Min(p => (int?)p.PriceCents);
                if (minPrice.HasValue)
                {
                    prices[group.Key] = minPrice.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching marketplace products for expansion {ExpansionId}", expansionId);
            throw;
        }

        // Calculate metrics
        int cardCount = 0;
        decimal totalMinPrice = 0;

        // PREPARE DEBUG BREAKDOWN
        var debugFile = Path.Combine(Directory.GetCurrentDirectory(), $"debug_expansion_{expansionId}.csv");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BlueprintId;Name;Version;MinPriceCents;MinPriceEuro");

        foreach (var blueprint in blueprints)
        {
            if (prices.TryGetValue(blueprint.CardTraderId, out int minPriceCents))
            {
                decimal priceEuro = (decimal)minPriceCents / 100m;
                sb.AppendLine($"{blueprint.CardTraderId};{blueprint.Name};{blueprint.Version};{minPriceCents};{priceEuro:F2}");
                totalMinPrice += priceEuro;
                cardCount++;
            }
            else
            {
                sb.AppendLine($"{blueprint.CardTraderId};{blueprint.Name};{blueprint.Version};0;0.00");
            }
        }

        try { File.WriteAllText(debugFile, sb.ToString()); } catch { /* Ignore debug file errors */ }

        _logger.LogInformation("Analysis results for {ExpansionName}: {CardCount} cards with prices found out of {TotalCards} blueprints. Total: €{Total:F2}. Breakdown saved to: {DebugFile}",
            expansion.Name, cardCount, blueprints.Count, totalMinPrice, debugFile);

        expansion.TotalMinPrice = totalMinPrice;
        expansion.AverageCardValue = cardCount > 0 ? totalMinPrice / cardCount : 0;
        expansion.LastValueAnalysisUpdate = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ExpansionAnalysisResult
        {
            BlueprintCount = blueprints.Count,
            CardsAnalyzedCount = cardCount,
            AverageValue = expansion.AverageCardValue ?? 0,
            TotalValue = expansion.TotalMinPrice ?? 0
        };
    }

    public async Task<AnalyticsSyncResult> AnalyzeAllExpansionsValueAsync(CancellationToken cancellationToken = default)
    {
        var expansions = await _dbContext.Expansions
            .Include(e => e.Game)
            .Where(e => e.Game.IsEnabled)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Starting bulk analysis for {Count} enabled expansions", expansions.Count);

        var result = new AnalyticsSyncResult();

        foreach (var expansion in expansions)
        {
            try
            {
                await AnalyzeExpansionValueAsync(expansion.Id, cancellationToken);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze expansion {ExpansionName}", expansion.Name);
                result.FailedCount++;
            }
        }

        return result;
    }
}
