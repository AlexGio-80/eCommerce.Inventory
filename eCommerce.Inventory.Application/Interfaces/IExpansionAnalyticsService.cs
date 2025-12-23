namespace eCommerce.Inventory.Application.Interfaces;

public interface IExpansionAnalyticsService
{
    /// <summary>
    /// Analyzes the average card value for a specific expansion
    /// </summary>
    Task<ExpansionAnalysisResult> AnalyzeExpansionValueAsync(int expansionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes the average card value for all enabled expansions
    /// </summary>
    Task<AnalyticsSyncResult> AnalyzeAllExpansionsValueAsync(CancellationToken cancellationToken = default);
}

public class ExpansionAnalysisResult
{
    public int BlueprintCount { get; set; }
    public int CardsAnalyzedCount { get; set; }
    public decimal AverageValue { get; set; }
    public decimal TotalValue { get; set; }
}

public class AnalyticsSyncResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}
