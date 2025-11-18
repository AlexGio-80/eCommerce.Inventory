namespace eCommerce.Inventory.Domain.Entities;

public class Blueprint
{
    public int Id { get; set; }
    public int CardTraderId { get; set; }
    public string Name { get; set; }
    public string Version { get; set; } // Per varianti come "Showcase", "Borderless", etc.
    public string Rarity { get; set; }

    public int ExpansionId { get; set; }
    public virtual Expansion Expansion { get; set; }
    public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
}
