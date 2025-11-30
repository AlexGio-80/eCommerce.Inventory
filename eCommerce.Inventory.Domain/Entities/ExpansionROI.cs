namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Entity mapping for dbo.ExpansionsROI database view
/// </summary>
public class ExpansionROI
{
    public string ExpansionName { get; set; } = string.Empty;
    public decimal Differenza { get; set; }
    public decimal TotaleVenduto { get; set; }
    public decimal TotaleAcquistato { get; set; }
}
