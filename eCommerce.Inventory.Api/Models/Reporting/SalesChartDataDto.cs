namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Time-series data for sales charts
/// </summary>
public class SalesChartDataDto
{
    public List<string> Labels { get; set; } = new();
    public List<decimal> Values { get; set; } = new();
    public string GroupBy { get; set; } = "day"; // day, week, month
}
