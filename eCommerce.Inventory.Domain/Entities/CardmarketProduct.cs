namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Prodotto del catalogo pubblico di Cardmarket di cui teniamo lo storico prezzi.
///
/// Non è il catalogo completo: ci sono tutti i prodotti sigillati, che servono all'analisi degli
/// acquisti, e solo le singole delle espansioni recenti o in preordine. Le altre singole non
/// servono: per la vendita il riferimento resta il mercato Card Trader.
///
/// L'abbinamento con Card Trader non sta qui ma su <see cref="Blueprint.CardMarketIds"/>, che
/// Card Trader valorizza anche per i sigillati.
/// </summary>
public class CardmarketProduct
{
    /// <summary>Id del prodotto su Cardmarket (<c>idProduct</c>): è anche la chiave primaria.</summary>
    public int IdProduct { get; set; }

    public string Name { get; set; } = string.Empty;

    public int IdCategory { get; set; }

    /// <summary>Es. "Magic Display" per i box, "Magic Booster" per le buste, "Magic Single" per le carte.</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Id dell'espansione su Cardmarket: non coincide con quello di Card Trader.</summary>
    public int IdExpansion { get; set; }

    /// <summary>Data in cui Cardmarket ha aggiunto il prodotto al catalogo, spesso prima dell'uscita.</summary>
    public DateTime? DateAdded { get; set; }

    /// <summary>True per le carte singole, false per i prodotti sigillati.</summary>
    public bool IsSingle { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
