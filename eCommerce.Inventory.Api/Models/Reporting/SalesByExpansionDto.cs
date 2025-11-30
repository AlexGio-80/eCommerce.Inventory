namespace eCommerce.Inventory.Api.Models.Reporting;

public class SalesByExpansionDto
{
    public string ExpansionName { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int OrderCount { get; set; }
    public decimal Percentage { get; set; }
}
