namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Blueprint (Card) API response
/// </summary>
public class CardTraderBlueprintDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string ImageUrl { get; set; }
    public string Rarity { get; set; }
    public int ExpansionId { get; set; }
}
