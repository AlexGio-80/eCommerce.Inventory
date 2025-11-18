namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Expansion/Set API response
/// </summary>
public class CardTraderExpansionDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Abbreviation { get; set; } // Es. "MOM", "LTR"
    public int GameId { get; set; }
}
