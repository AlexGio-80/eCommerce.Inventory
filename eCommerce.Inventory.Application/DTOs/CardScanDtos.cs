namespace eCommerce.Inventory.Application.DTOs;

/// <summary>
/// Quello che l'IA ha letto sulla foto di una carta. Non è ancora una carta del database: la
/// corrispondenza con i blueprint la fa il backend, così una lettura sbagliata non può mai
/// inventare una carta che non esiste.
/// </summary>
public class CardReading
{
    /// <summary>Nome inglese della carta (anche se la stampa è in un'altra lingua, quando è noto).</summary>
    public string? Name { get; set; }

    /// <summary>Nome come stampato sulla carta, nella sua lingua.</summary>
    public string? PrintedName { get; set; }

    /// <summary>Sigla del set stampata in basso a sinistra (es. "2x2"), minuscola.</summary>
    public string? SetCode { get; set; }

    /// <summary>Numero di raccolta, la parte prima della barra (es. "117").</summary>
    public string? CollectorNumber { get; set; }

    /// <summary>Lingua con i nomi della maschera "Nuovo Prodotto" (English, Italian, ...).</summary>
    public string? Language { get; set; }

    public bool IsFoil { get; set; }

    /// <summary>"high", "medium" o "low": quanto l'IA è sicura della lettura.</summary>
    public string? Confidence { get; set; }

    /// <summary>Cosa non si leggeva, o cosa l'ha fatta dubitare.</summary>
    public string? Notes { get; set; }
}

/// <summary>Una carta del database che corrisponde alla lettura.</summary>
public class ScanCandidateDto
{
    public int BlueprintId { get; set; }
    public int CardTraderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ItalianName { get; set; }
    public string? Version { get; set; }
    public string ExpansionName { get; set; } = string.Empty;
    public string ExpansionCode { get; set; } = string.Empty;
    public string? CollectorNumber { get; set; }
    public string? Rarity { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Corrispondono set e numero di raccolta: è quasi certamente la stampa giusta.</summary>
    public bool ExactPrint { get; set; }
}

public class CardScanResultDto
{
    public CardReading Reading { get; set; } = new();
    public List<ScanCandidateDto> Candidates { get; set; } = new();

    /// <summary>Riconoscimenti fatti oggi e limite giornaliero (<c>Scan:DailyLimit</c>).</summary>
    public int UsedToday { get; set; }
    public int DailyLimit { get; set; }
}

/// <summary>Scheda di valutazione di una carta, per condizione, lingua e foil scelti.</summary>
public class CardValuationDto
{
    public ScanCandidateDto Card { get; set; } = new();
    public string Condition { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsFoil { get; set; }

    /// <summary>Prezzo delle regole dell'autopricer, lo stesso della maschera "Nuovo Prodotto".</summary>
    public ListingPriceSuggestionDto? RulesPrice { get; set; }

    /// <summary>Offerte Card Trader della stessa condizione, lingua e foil, escluse le mie. Prezzi in vetrina.</summary>
    public int CtOffers { get; set; }
    public decimal? CtLowest { get; set; }
    public decimal? CtZeroLowest { get; set; }

    /// <summary>Ultimo listino Cardmarket (foil se la carta è foil). Null se la carta non è abbinata.</summary>
    public decimal? CmTrend { get; set; }
    public decimal? CmLow { get; set; }
    public DateOnly? CmDate { get; set; }

    /// <summary>Le mie inserzioni della stessa carta, di qualsiasi condizione.</summary>
    public List<MyListingDto> MyListings { get; set; } = new();
}

public class MyListingDto
{
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsFoil { get; set; }
}
