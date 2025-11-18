namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Game API response
/// </summary>
public class CardTraderGameDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Abbreviation { get; set; } // Es. "mtg", "ygo"
}
