namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Top performing products by profitability
/// </summary>
public class TopPerformerDto
{
    public int BlueprintId { get; set; }
    public string CardName { get; set; } = string.Empty;
    public string ExpansionName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal ProfitMarginPercentage { get; set; }
}
