// --- Entità di Supporto (Dati da Card Trader) ---

public class Game
{
    public int Id { get; set; } // La nostra chiave primaria interna
    public int CardTraderId { get; set; } // L'ID di Card Trader
    public string Name { get; set; }
    public string Code { get; set; } // Es. "mtg", "ygo"

    public virtual ICollection<Expansion> Expansions { get; set; }
}

public class Expansion
{
    public int Id { get; set; } // La nostra chiave primaria interna
    public int CardTraderId { get; set; } // L'ID di Card Trader
    public string Name { get; set; }
    public string Code { get; set; } // Es. "MOM", "LTR"

    public int GameId { get; set; }
    public virtual Game Game { get; set; }
    public virtual ICollection<Blueprint> Blueprints { get; set; }
}

public class Blueprint
{
    public int Id { get; set; } // La nostra chiave primaria interna
    public int CardTraderId { get; set; } // L'ID del Blueprint su Card Trader
    public string Name { get; set; }
    public string Version { get; set; } // Per varianti come "Showcase", "Borderless", etc.
    public string Rarity { get; set; }

    public int ExpansionId { get; set; }
    public virtual Expansion Expansion { get; set; }
    public virtual ICollection<InventoryItem> InventoryItems { get; set; }
}

// --- Entità Principali del Tuo Inventario e Vendite ---

public class InventoryItem
{
    public int Id { get; set; } // La nostra chiave primaria interna
    public int? CardTraderProductId { get; set; } // L'ID del prodotto su Card Trader (può essere nullo se non ancora in vendita)

    // Link al blueprint (la carta che sto vendendo)
    public int BlueprintId { get; set; }
    public virtual Blueprint Blueprint { get; set; }

    // Dati specifici del MIO oggetto
    public decimal PurchasePrice { get; set; } // Prezzo a cui l'ho comprato
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public int Quantity { get; set; }
    public decimal ListingPrice { get; set; } // Prezzo a cui lo metto in vendita

    // Proprietà da inviare a Card Trader
    public string Condition { get; set; } // Es. "Near Mint", "Played"
    public string Language { get; set; } // Es. "English", "Italian"
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }

    // Dati personalizzati per la BI
    public string Location { get; set; } // Es. "Scatola A", "Raccoglitore 1" (corrisponde a user_data_field)
}

public class Order
{
    public int Id { get; set; } // La nostra chiave primaria interna
    public int CardTraderOrderId { get; set; } // L'ID dell'ordine su Card Trader
    public DateTime DatePlaced { get; set; }
    public string Status { get; set; } // Es. "Paid", "Shipped", "Cancelled"
    public decimal TotalAmount { get; set; }
    public decimal ShippingCost { get; set; }

    public virtual ICollection<OrderItem> OrderItems { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public virtual Order Order { get; set; }

    // Link all'item di inventario venduto
    public int InventoryItemId { get; set; }
    public virtual InventoryItem InventoryItem { get; set; }

    public int QuantitySold { get; set; }
    public decimal PricePerItem { get; set; } // Prezzo al momento della vendita
}