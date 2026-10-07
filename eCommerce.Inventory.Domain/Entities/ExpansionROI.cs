namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Entity mapping for dbo.ExpansionsROI database view
///
/// Della vista si legge solo il venduto. Le sue colonne <c>TotaleAcquistato</c> e <c>Differenza</c>
/// sommano il costo anche delle modifiche fatte dalla maschera, la cui quantità è il nuovo totale
/// dell'inserzione e non le copie aggiunte: non sono mappate apposta, il costo si prende da
/// <c>PurchaseCostService</c>.
/// </summary>
public class ExpansionROI
{
    public string ExpansionName { get; set; } = string.Empty;
    public decimal? TotaleVenduto { get; set; }
}
