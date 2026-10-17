namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// L'ultima lettura della vista "Per venditore" (feature 007): le offerte Card Trader lette per espansione, che costano
/// circa 7 minuti. Salvata a fine ricerca, così dopo un riavvio o una pubblicazione la scheda la riapre e l'utente
/// rilegge Card Trader solo quando vuole offerte fresche. Se ne tiene una sola.
/// </summary>
public class SellerPlanSnapshot
{
    public int Id { get; set; }

    /// <summary>Id della lettura, lo stesso che usa la schermata per ricalcolare.</summary>
    public string RunId { get; set; } = string.Empty;

    public DateTime CollectedAt { get; set; }

    /// <summary>Regole, budget e spedizioni dell'ultima richiesta (JSON).</summary>
    public string RequestJson { get; set; } = string.Empty;

    /// <summary>Prodotti, offerte e drop Wizards letti (JSON).</summary>
    public string DataJson { get; set; } = string.Empty;
}
