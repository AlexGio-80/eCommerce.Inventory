namespace eCommerce.Inventory.Domain.Entities;

public class InventoryItem
{
    public int Id { get; set; }
    public int? CardTraderProductId { get; set; }

    // Link al blueprint (la carta che sto vendendo)
    public int BlueprintId { get; set; }
    public virtual Blueprint Blueprint { get; set; }

    // Dati specifici del MIO oggetto
    public decimal PurchasePrice { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public int Quantity { get; set; }
    public decimal ListingPrice { get; set; }

    // Proprietà da inviare a Card Trader
    public string Condition { get; set; } // Es. "Near Mint", "Played"
    public string Language { get; set; } // Es. "English", "Italian"
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }

    // Dati personalizzati per la BI
    public string Location { get; set; } // Es. "Scatola A", "Raccoglitore 1"

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
