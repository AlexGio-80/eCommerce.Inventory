namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Order API response
/// </summary>
public class CardTraderOrderDto
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string State { get; set; } // Paid, Shipped, Cancelled, etc.
    public decimal Total { get; set; }
    public decimal ShippingPrice { get; set; }
    public List<CardTraderOrderItemDto> Items { get; set; } = new();
}

public class CardTraderOrderItemDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
