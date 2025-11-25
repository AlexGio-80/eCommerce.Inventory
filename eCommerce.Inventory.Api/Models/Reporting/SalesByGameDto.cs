namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Sales distribution by game
/// </summary>
public class SalesByGameDto
{
    public string GameName { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int OrderCount { get; set; }
    public decimal Percentage { get; set; }
}
