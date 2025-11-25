namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Sales metrics for a given date range
/// </summary>
public class SalesMetricsDto
{
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public decimal AverageOrderValue { get; set; }
    public decimal GrowthPercentage { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}
