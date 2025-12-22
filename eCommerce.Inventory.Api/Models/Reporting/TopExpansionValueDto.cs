namespace eCommerce.Inventory.Api.Models.Reporting;

public class TopExpansionValueDto
{
    public int ExpansionId { get; set; }
    public string ExpansionName { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public decimal AverageCardValue { get; set; }
    public decimal TotalMinPrice { get; set; }
    public DateTime? LastUpdate { get; set; }
}
