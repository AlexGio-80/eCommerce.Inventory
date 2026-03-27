namespace eCommerce.Inventory.Api.Models.Reporting;

public class TagExpansionProfitabilityDto
{
    public string ExpansionName { get; set; } = string.Empty;
    public decimal TotaleAcquistato { get; set; }
    public decimal TotaleVenduto { get; set; }
    public decimal Differenza { get; set; }
    public decimal PercentualeDifferenza { get; set; }
    public int QuantitaVenduta { get; set; }
    public int QtaRimanente { get; set; }
    public decimal ValoreRimanente { get; set; }
}
