namespace eCommerce.Inventory.Application.DTOs;

public class MarketplaceStatsDto
{
    public int BlueprintId { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal AveragePrice { get; set; }
    public int TotalListings { get; set; }
    public string Currency { get; set; } = "EUR";
}
