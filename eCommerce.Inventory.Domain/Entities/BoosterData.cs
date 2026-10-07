namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Carta secondo MTGJSON, con gli id per ritrovarne il prezzo: Cardmarket (<see cref="CardmarketId"/>)
/// e Card Trader (tramite <see cref="ScryfallId"/> su <see cref="Blueprint.ScryfallId"/>).
/// Si importano solo le carte dei set delle uscite analizzate, non tutto il catalogo.
/// </summary>
public class MtgjsonCard
{
    public Guid Uuid { get; set; }
    public string SetCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? Rarity { get; set; }
    public int? CardmarketId { get; set; }
    public string? ScryfallId { get; set; }
}

/// <summary>
/// Una delle configurazioni possibili di un tipo di busta (es. Play Booster: 7 comuni, 3 non
/// comuni, ... con una terra foil al posto di quella normale una volta su cinque).
/// </summary>
public class BoosterConfig
{
    public int Id { get; set; }
    public string SetCode { get; set; } = string.Empty;

    /// <summary>Tipo di busta come nel contenuto dei sigillati (es. "play", "collector").</summary>
    public string BoosterType { get; set; } = string.Empty;

    public int Weight { get; set; }

    /// <summary>Somma dei pesi delle configurazioni dello stesso tipo: probabilità = Weight / TotalWeight.</summary>
    public int TotalWeight { get; set; }

    public ICollection<BoosterConfigSlot> Slots { get; set; } = new List<BoosterConfigSlot>();
}

public class BoosterConfigSlot
{
    public int Id { get; set; }
    public int BoosterConfigId { get; set; }
    public BoosterConfig? BoosterConfig { get; set; }

    /// <summary>Nome del foglio di stampa da cui si pesca (es. "rareMythic").</summary>
    public string SheetName { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Foglio di stampa: le carte che possono uscire in uno slot, con il loro peso.</summary>
public class BoosterSheet
{
    public int Id { get; set; }
    public string SetCode { get; set; } = string.Empty;
    public string BoosterType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFoil { get; set; }
    public long TotalWeight { get; set; }

    public ICollection<BoosterSheetCard> Cards { get; set; } = new List<BoosterSheetCard>();
}

public class BoosterSheetCard
{
    public int Id { get; set; }
    public int BoosterSheetId { get; set; }
    public BoosterSheet? BoosterSheet { get; set; }
    public Guid CardUuid { get; set; }

    /// <summary>Probabilità della carta nello slot = Weight / <see cref="BoosterSheet.TotalWeight"/>.</summary>
    public long Weight { get; set; }
}

/// <summary>Mazzo a contenuto fisso (Commander, Scene Box, Welcome Deck, ...).</summary>
public class MtgjsonDeck
{
    public int Id { get; set; }
    public string SetCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ICollection<MtgjsonDeckCard> Cards { get; set; } = new List<MtgjsonDeckCard>();
}

public class MtgjsonDeckCard
{
    public int Id { get; set; }
    public int MtgjsonDeckId { get; set; }
    public MtgjsonDeck? MtgjsonDeck { get; set; }
    public Guid CardUuid { get; set; }
    public int Count { get; set; }
    public bool IsFoil { get; set; }
}

/// <summary>
/// Ultimo prezzo Cardmarket di ogni prodotto del listino, senza storico. Lo storico tiene le sole
/// singole recenti; il valore atteso di un'espansione vecchia ha bisogno anche delle altre.
/// </summary>
public class CardmarketLatestPrice
{
    public int IdProduct { get; set; }

    /// <summary>Giorno del listino in cui il prezzo è cambiato l'ultima volta.</summary>
    public DateOnly Date { get; set; }

    public decimal? Trend { get; set; }
    public decimal? Low { get; set; }
    public decimal? TrendFoil { get; set; }
    public decimal? LowFoil { get; set; }
}

/// <summary>
/// Prezzo di una singola sul marketplace Card Trader (inglese, Near Mint), aggiornato a richiesta
/// insieme ai sigillati. Scala vetrina: quanto paga chi compra.
/// </summary>
public class CardTraderCardPrice
{
    /// <summary>Id del blueprint su Card Trader.</summary>
    public int BlueprintId { get; set; }
    public bool IsFoil { get; set; }

    /// <summary>Media delle tre offerte più basse: meno esposta del minimo a un venditore isolato.</summary>
    public decimal Price { get; set; }
    public int OfferCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
