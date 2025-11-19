namespace eCommerce.Inventory.Domain.Entities;

public class Game
{
    public int Id { get; set; }
    public int CardTraderId { get; set; }
    public string Name { get; set; }
    public string Code { get; set; } // Es. "mtg", "ygo"

    /// <summary>
    /// Controls whether this game's expansions and blueprints should be synchronized
    /// Set to false by default to avoid importing unnecessary data
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    public virtual ICollection<Expansion> Expansions { get; set; } = new List<Expansion>();
    public virtual ICollection<Category> Categories { get; set; } = new List<Category>();
}
