namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Represents a product category from Card Trader API
/// Categories classify products within a game (e.g., "Magic Single Card", "Magic Token/Emblem")
/// Each category has properties that define attributes for products in that category
/// </summary>
public class Category
{
    public int Id { get; set; }
    public int CardTraderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GameId { get; set; }

    // Navigation properties
    public virtual Game Game { get; set; } = null!;
    public virtual ICollection<Property> Properties { get; set; } = new List<Property>();
}
