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

        _logger.LogInformation("Starting value analysis for expansion {ExpansionName} ({ExpansionId})", expansion.Name, expansionId);

        decimal totalMinPrice = 0;
        int cardCount = 0;

        foreach (var blueprint in expansion.Blueprints)
        {
            try
            {
                var stats = await _cardTraderApiService.GetMarketplaceProductsAsync(blueprint.CardTraderId, cancellationToken);

                // Group by blueprint and calculate min
                if (stats != null && stats.Any())
                {
                    var minPriceCents = stats.Where(p => p.PriceCents > 0).Min(p => (int?)p.PriceCents) ?? 0;
                    if (minPriceCents > 0)
                    {
                        totalMinPrice += (decimal)minPriceCents / 100m;
                        cardCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching marketplace stats for blueprint {BlueprintId}", blueprint.CardTraderId);
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
