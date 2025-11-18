namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Product (Inventory Item) API response
/// </summary>
public class CardTraderProductDto
{
    public int Id { get; set; }
    public int BlueprintId { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Condition { get; set; }
    public string Language { get; set; }
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }
    public string UserDataField { get; set; } // Location
    public DateTime UpdatedAt { get; set; }
}
