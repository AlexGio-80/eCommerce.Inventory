namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Offerta di un venditore Cardmarket letta da una pagina incollata dall'utente (feature 007, Fase 1).
///
/// Cardmarket non dà i venditori nel listino pubblico e blocca la lettura automatica delle pagine: l'utente
/// apre le pagine delle offerte del venditore, copia e incolla. Le offerte valgono pochi giorni, poi il
/// pianificatore le ignora; un nuovo incolla dello stesso venditore sostituisce quelle dei prodotti che contiene.
/// </summary>
public class CardmarketSellerOffer
{
    public int Id { get; set; }

    /// <summary>Nome utente del venditore su Cardmarket, come nella pagina.</summary>
    public string Seller { get; set; } = string.Empty;

    /// <summary>Prodotto del listino Cardmarket a cui il nome della pagina è stato abbinato.</summary>
    public int IdProduct { get; set; }

    /// <summary>Nome come nella pagina.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Categoria della pagina (es. "Preconstructed Decks", "Sets").</summary>
    public string? Category { get; set; }

    public decimal Price { get; set; }

    public int Quantity { get; set; }

    /// <summary>Commento del venditore; "DAMAGED!" o simili escludono l'offerta dal piano.</summary>
    public string? Comment { get; set; }

    /// <summary>Danneggiato secondo il commento: resta visibile, ma non si propone.</summary>
    public bool IsDamaged { get; set; }

    public DateTime PastedAt { get; set; } = DateTime.UtcNow;
}
