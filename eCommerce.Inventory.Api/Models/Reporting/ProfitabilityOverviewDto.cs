namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Overall profitability metrics
/// </summary>
public class ProfitabilityOverviewDto
{
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal ProfitMarginPercentage { get; set; }
    public decimal ROI { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
}
