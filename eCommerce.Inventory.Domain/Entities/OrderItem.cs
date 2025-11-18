namespace eCommerce.Inventory.Domain.Entities;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public virtual Order Order { get; set; }

    // Link all'item di inventario venduto
    public int InventoryItemId { get; set; }
    public virtual InventoryItem InventoryItem { get; set; }

    public int QuantitySold { get; set; }
    public decimal PricePerItem { get; set; }
}
