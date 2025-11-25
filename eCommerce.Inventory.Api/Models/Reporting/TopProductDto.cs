namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Top performing product information
/// </summary>
public class TopProductDto
{
    public int BlueprintId { get; set; }
    public string CardName { get; set; } = string.Empty;
    public string ExpansionName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public int QuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AveragePrice { get; set; }
}
