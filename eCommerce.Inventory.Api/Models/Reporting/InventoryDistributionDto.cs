namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Inventory distribution by game
/// </summary>
public class InventoryDistributionDto
{
    public string GameName { get; set; } = string.Empty;
    public decimal TotalValue { get; set; }
    public int ItemCount { get; set; }
    public decimal Percentage { get; set; }
}
