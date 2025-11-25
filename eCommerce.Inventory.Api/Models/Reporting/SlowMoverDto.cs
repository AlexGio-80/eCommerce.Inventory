namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Slow-moving inventory items
/// </summary>
public class SlowMoverDto
{
    public int InventoryItemId { get; set; }
    public string CardName { get; set; } = string.Empty;
    public string ExpansionName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ListingPrice { get; set; }
    public int DaysInInventory { get; set; }
    public DateTime DateAdded { get; set; }
}
