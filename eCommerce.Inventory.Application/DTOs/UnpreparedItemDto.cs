namespace eCommerce.Inventory.Application.DTOs;

public class UnpreparedItemDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? ExpansionName { get; set; }
    public string? Condition { get; set; }
    public string? Language { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? OrderCode { get; set; }
    public string? BuyerUsername { get; set; }
    public DateTime? OrderDate { get; set; }
    public bool IsPrepared { get; set; }
    public string? ImageUrl { get; set; }
}
