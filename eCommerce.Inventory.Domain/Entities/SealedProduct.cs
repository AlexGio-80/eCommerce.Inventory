namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Espansione secondo MTGJSON. Serve a raggruppare un'uscita: un set "figlio" (es. Star Trek
/// Commander, <c>TRC</c>) punta al padre (Star Trek, <c>TRK</c>) con <see cref="ParentCode"/>, e i
/// prodotti dei due vanno valutati insieme quando si decide cosa comprare.
/// </summary>
public class MtgjsonSet
{
    /// <summary>Codice MTGJSON in maiuscolo (es. "TRK"): chiave primaria.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? ParentCode { get; set; }
    public string? Type { get; set; }
    public DateOnly? ReleaseDate { get; set; }

    public DateTime LastImportedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Prodotto sigillato (busta, box, bundle, mazzo Commander, Scene Box, case...) dal catalogo
/// MTGJSON, con il suo contenuto e gli id con cui ritrovarlo su Cardmarket e Card Trader.
///
/// Il contenuto viene da MTGJSON e non da altre fonti perché è l'unica che lo dà strutturato:
/// le analisi fatte a mano partivano da conteggi chiesti a un'IA generica, ed erano sbagliati.
/// </summary>
public class SealedProduct
{
    public int Id { get; set; }

    /// <summary>Uuid MTGJSON: è il riferimento con cui un prodotto ne contiene un altro.</summary>
    public Guid Uuid { get; set; }

    /// <summary>Codice MTGJSON dell'espansione, in maiuscolo.</summary>
    public string SetCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Es. booster_box, booster_pack, bundle, deck, box_set, booster_case.</summary>
    public string? Category { get; set; }

    /// <summary>Es. play, collector, draft, commander.</summary>
    public string? Subtype { get; set; }

    public int? CardCount { get; set; }

    /// <summary>Id del prodotto su Cardmarket (<c>idProduct</c>): collega allo storico prezzi CM.</summary>
    public int? CardmarketId { get; set; }

    /// <summary>Id del blueprint su Card Trader.</summary>
    public int? CardTraderBlueprintId { get; set; }

    public DateTime LastImportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Prezzo più basso in inglese sul marketplace Card Trader, scala vetrina (quanto paga chi
    /// compra). Aggiornato solo a richiesta: costa chiamate al limite condiviso di 20 al minuto.
    /// </summary>
    public decimal? CtMinPrice { get; set; }
    public int? CtOfferCount { get; set; }
    public DateTime? CtPriceUpdatedAt { get; set; }

    public ICollection<SealedProductContent> Contents { get; set; } = new List<SealedProductContent>();
}

/// <summary>Una voce del contenuto di un prodotto sigillato, come la riporta MTGJSON.</summary>
public class SealedProductContent
{
    public int Id { get; set; }

    public int SealedProductId { get; set; }
    public SealedProduct? SealedProduct { get; set; }

    public SealedContentKind Kind { get; set; }

    /// <summary>Quantità per <see cref="SealedContentKind.Sealed"/>; 1 per le altre voci.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Nome della voce (mazzo, carta, prodotto contenuto, extra come "90 Non-foil basic lands").</summary>
    public string? Name { get; set; }

    /// <summary>Codice dell'espansione della voce, in maiuscolo.</summary>
    public string? SetCode { get; set; }

    /// <summary>Tipo di busta per <see cref="SealedContentKind.Pack"/> (es. "play", "collector").</summary>
    public string? PackCode { get; set; }

    /// <summary>Prodotto contenuto per <see cref="SealedContentKind.Sealed"/>, o carta per <see cref="SealedContentKind.Card"/>.</summary>
    public Guid? ChildUuid { get; set; }

    public bool? Foil { get; set; }
}

public enum SealedContentKind
{
    /// <summary>Una busta generata secondo la composizione del suo tipo (Fase 2).</summary>
    Pack = 0,
    /// <summary>Un altro prodotto sigillato, con la sua quantità.</summary>
    Sealed = 1,
    /// <summary>Un mazzo a contenuto fisso.</summary>
    Deck = 2,
    /// <summary>Una carta specifica.</summary>
    Card = 3,
    /// <summary>Extra senza valore di rivendita autonomo: terre base, dadi, scatole, guide.</summary>
    Other = 4,
    /// <summary>Contenuto a scelta fra più configurazioni: non scomponibile in modo univoco.</summary>
    Variable = 5
}
