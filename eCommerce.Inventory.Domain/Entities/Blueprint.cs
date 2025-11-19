namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Blueprint represents a card template with all its metadata from Card Trader
/// Each Blueprint corresponds to a unique card (identified by Card Trader ID)
/// </summary>
public class Blueprint
{
    public int Id { get; set; }

    /// <summary>
    /// Card Trader unique identifier for this blueprint
    /// </summary>
    public int CardTraderId { get; set; }

    /// <summary>
    /// Card name
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Card version/variant (e.g., "Showcase", "Borderless", null for regular)
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Card Game identifier (foreign key to Game)
    /// </summary>
    public int GameId { get; set; }
    public virtual Game Game { get; set; }

    /// <summary>
    /// Category identifier (defines what properties are editable for this card)
    /// </summary>
    public int CategoryId { get; set; }

    /// <summary>
    /// Rarity of the card (e.g., "Mythic", "Rare", "Uncommon", "Common")
    /// Note: This is extracted from FixedProperties for quick access
    /// </summary>
    public string? Rarity { get; set; }

    /// <summary>
    /// Fixed properties stored as JSON
    /// Contains immutable properties like rarity, collector_number, etc.
    /// Used to display card details and for future reference
    /// Example: { "mtg_rarity": "Mythic", "collector_number": "127" }
    /// </summary>
    public string? FixedProperties { get; set; } // JSON string

    /// <summary>
    /// Editable properties stored as JSON
    /// Contains schema of properties that can be set when creating a listing
    /// Used during product creation workflow to validate user input
    /// Example: [{ "name": "condition", "type": "string", "possible_values": [...] }]
    /// </summary>
    public string? EditableProperties { get; set; } // JSON string

    /// <summary>
    /// Card Market IDs (array stored as JSON)
    /// Multiple IDs if card exists on Cardmarket platform
    /// </summary>
    public string? CardMarketIds { get; set; } // JSON string (array)

    /// <summary>
    /// TCGPlayer ID for this card
    /// </summary>
    public int? TcgPlayerId { get; set; }

    /// <summary>
    /// Scryfall ID (for Magic: The Gathering cards)
    /// Used to link with Scryfall for additional data
    /// </summary>
    public string? ScryfallId { get; set; }

    /// <summary>
    /// Public URL to card image
    /// Hosted on Card Trader CDN, safe to display
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Back image URL (for double-faced cards)
    /// </summary>
    public string? BackImageUrl { get; set; }

    /// <summary>
    /// Expansion reference
    /// </summary>
    public int ExpansionId { get; set; }
    public virtual Expansion Expansion { get; set; }

    /// <summary>
    /// Inventory items created from this blueprint
    /// One blueprint can have multiple inventory items (different conditions, languages, etc.)
    /// </summary>
    public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();

    /// <summary>
    /// Timestamp when this blueprint was first created in the system
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this blueprint was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
