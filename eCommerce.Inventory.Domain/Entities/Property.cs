namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Represents a property/attribute within a category
/// Properties define what attributes can be used to describe products in a category
/// Example: "mtg_rarity", "condition", "mtg_language", "mtg_foil"
/// </summary>
public class Property
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "string", "boolean", etc.
    public int CategoryId { get; set; }

    // Navigation properties
    public virtual Category Category { get; set; } = null!;
    public virtual ICollection<PropertyValue> PossibleValues { get; set; } = new List<PropertyValue>();
}
