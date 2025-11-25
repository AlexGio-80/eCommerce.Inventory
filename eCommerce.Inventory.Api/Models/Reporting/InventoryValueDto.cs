namespace eCommerce.Inventory.Api.Models.Reporting;

/// <summary>
/// Current inventory value metrics
/// </summary>
public class InventoryValueDto
{
    public decimal TotalValue { get; set; }
    public int TotalItems { get; set; }
    public int UniqueProducts { get; set; }
    public decimal AverageItemValue { get; set; }
}
