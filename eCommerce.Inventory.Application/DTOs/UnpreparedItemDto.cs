namespace eCommerce.Inventory.Application.DTOs;

public class UnpreparedItemDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? ExpansionName { get; set; }
    public string? ExpansionCode { get; set; }
    public string? Condition { get; set; }
    public string? Language { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? OrderCode { get; set; }
    public string? BuyerUsername { get; set; }
    public DateTime? OrderDate { get; set; }
    public bool IsPrepared { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }
    public bool IsAltered { get; set; }
    public string? Tag { get; set; }
    public string? CollectorNumber { get; set; }
    public int? CardTraderBlueprintId { get; set; }
    public DateTime? ExpansionReleaseDate { get; set; }
    public string? IconSvgUri { get; set; }
}
