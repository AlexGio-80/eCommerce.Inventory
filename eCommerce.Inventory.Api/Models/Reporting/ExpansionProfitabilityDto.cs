namespace eCommerce.Inventory.Api.Models.Reporting;

public class ExpansionProfitabilityDto
{
    public string ExpansionName { get; set; } = string.Empty;
    public decimal Differenza { get; set; }
    public decimal TotaleVenduto { get; set; }
    public decimal TotaleAcquistato { get; set; }
    public decimal PercentualeDifferenza { get; set; }
}
