namespace eCommerce.Inventory.Domain.Entities;

public class Game
{
    public int Id { get; set; }
    public int CardTraderId { get; set; }
    public string Name { get; set; }
    public string Code { get; set; } // Es. "mtg", "ygo"

    public virtual ICollection<Expansion> Expansions { get; set; } = new List<Expansion>();
}
