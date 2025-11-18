namespace eCommerce.Inventory.Domain.Entities;

public class Expansion
{
    public int Id { get; set; }
    public int CardTraderId { get; set; }
    public string Name { get; set; }
    public string Code { get; set; } // Es. "MOM", "LTR"

    public int GameId { get; set; }
    public virtual Game Game { get; set; }
    public virtual ICollection<Blueprint> Blueprints { get; set; } = new List<Blueprint>();
}
