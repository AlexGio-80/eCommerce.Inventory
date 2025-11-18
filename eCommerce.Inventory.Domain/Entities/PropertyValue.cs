namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Represents a possible value for a property within a category
/// Example: For property "mtg_rarity", possible values are: "Common", "Uncommon", "Rare", "Mythic", etc.
/// </summary>
public class PropertyValue
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public int PropertyId { get; set; }

    // Navigation properties
    public virtual Property Property { get; set; } = null!;
}
