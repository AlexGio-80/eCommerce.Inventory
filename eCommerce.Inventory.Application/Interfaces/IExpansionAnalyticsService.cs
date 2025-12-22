namespace eCommerce.Inventory.Application.Interfaces;

public interface IExpansionAnalyticsService
{
    /// <summary>
    /// Analyzes the average card value for a specific expansion
    /// </summary>
    Task AnalyzeExpansionValueAsync(int expansionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes the average card value for all enabled expansions
    /// </summary>
    Task<AnalyticsSyncResult> AnalyzeAllExpansionsValueAsync(CancellationToken cancellationToken = default);
}

public class AnalyticsSyncResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}
