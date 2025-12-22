using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    public async Task AnalyzeExpansionValueAsync(int expansionId, CancellationToken cancellationToken = default)
    {
        var expansion = await _dbContext.Expansions
            .Include(e => e.Blueprints)
            .FirstOrDefaultAsync(e => e.Id == expansionId, cancellationToken);

        if (expansion == null)
        {
            _logger.LogWarning("Expansion {ExpansionId} not found for analysis", expansionId);
            return;
        }

        _logger.LogInformation("Starting value analysis for expansion {ExpansionName} ({ExpansionId}) - Blueprints: {Count}",
            expansion.Name, expansionId, expansion.Blueprints.Count);

        decimal totalMinPrice = 0;
        int cardCount = 0;

        // Batch blueprints to reduce API calls (e.g., 50 at a time)
        const int batchSize = 50;
        var blueprints = expansion.Blueprints.ToList();

        for (int i = 0; i < blueprints.Count; i += batchSize)
        {
            var batch = blueprints.Skip(i).Take(batchSize).ToList();
            var batchIds = batch.Select(b => b.CardTraderId).ToList();

            try
            {
                var allProducts = await _cardTraderApiService.GetMarketplaceProductsBatchAsync(batchIds, cancellationToken);

                // Group products by blueprint to find min price for each blueprint in the batch
                var productsByBlueprint = allProducts
                    .Where(p => p.PriceCents > 0)
                    .GroupBy(p => p.BlueprintId);

                foreach (var group in productsByBlueprint)
                {
                    var minPriceCents = group.Min(p => p.PriceCents);
                    if (minPriceCents > 0)
                    {
                        totalMinPrice += (decimal)minPriceCents / 100m;
                        cardCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching marketplace stats for batch starting at {Index}", i);
            }
        }

        if (cardCount > 0)
        {
            expansion.TotalMinPrice = totalMinPrice;
            expansion.AverageCardValue = totalMinPrice / cardCount;
            expansion.LastValueAnalysisUpdate = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Analysis completed for {ExpansionName}. Avg Value: {AvgValue}, Total Min: {TotalMin}, Cards analyzed: {CardCount}",
                expansion.Name, expansion.AverageCardValue, expansion.TotalMinPrice, cardCount);
        }
        else
        {
            _logger.LogWarning("No card prices found for expansion {ExpansionName}", expansion.Name);
        }
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
