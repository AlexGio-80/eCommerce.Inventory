namespace eCommerce.Inventory.Domain.Entities;

/// <summary>Cosa ha innescato la valutazione di prezzo.</summary>
public enum PricingTrigger
{
    /// <summary>Esecuzione notifica schedulata.</summary>
    Scheduled = 0,

    /// <summary>Vendita ricevuta: la carta è stata riprezzata subito dopo l'ordine.</summary>
    OrderReceived = 1,

    /// <summary>Richiesta manuale dall'interfaccia.</summary>
    Manual = 2,

    /// <summary>Anteprima: calcolo a scopo di verifica, mai applicato.</summary>
    Preview = 3
}

/// <summary>Esito della valutazione di una singola carta.</summary>
public enum PricingOutcome
{
    /// <summary>Prezzo modificato e scritto su Card Trader.</summary>
    Applied = 0,

    /// <summary>Variazione calcolata ma non scritta perché il profilo è in dry-run.</summary>
    SimulatedDryRun = 1,

    /// <summary>Il prezzo calcolato coincide con quello corrente: nessuna azione.</summary>
    NoChangeNeeded = 2,

    /// <summary>Nessuna regola copre la fascia di prezzo della carta.</summary>
    NoMatchingRule = 3,

    /// <summary>Offerte comparabili insufficienti per dedurre un prezzo affidabile.</summary>
    InsufficientOffers = 4,

    /// <summary>Bloccato dal guardrail sulla variazione massima per esecuzione.</summary>
    BlockedByGuardrail = 5,

    /// <summary>Bloccato dai flag CanIncrease/CanDecrease della regola.</summary>
    BlockedByDirection = 6,

    /// <summary>Errore durante la valutazione o la scrittura su Card Trader.</summary>
    Failed = 7
}

/// <summary>
/// Traccia ogni valutazione di prezzo, applicata o meno. È il registro che rende
/// verificabile la copertura: "questa carta non è stata aggiornata" diventa una query,
/// con il motivo accanto.
/// </summary>
public class PriceChangeLog
{
    public int Id { get; set; }

    /// <summary>
    /// Copia di magazzino valutata. Diventa null quando la carta esce dall'inventario, tipicamente
    /// perché è stata venduta: la riga di registro deve sopravvivere alla carta, altrimenti si perde
    /// lo storico proprio delle carte vendute, che sono quelle su cui ha più senso verificare
    /// se il prezzo proposto era corretto. La carta resta identificabile da <see cref="BlueprintId"/>.
    /// </summary>
    public int? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public int BlueprintId { get; set; }
    public Blueprint? Blueprint { get; set; }

    public int? PricingRunLogId { get; set; }
    public PricingRunLog? PricingRunLog { get; set; }

    /// <summary>Regola applicata. Null se nessuna regola copriva la fascia.</summary>
    public int? PricingRuleId { get; set; }

    public decimal OldPrice { get; set; }

    /// <summary>Prezzo calcolato dal motore, anche quando non è stato applicato.</summary>
    public decimal ProposedPrice { get; set; }

    public PricingTrigger Trigger { get; set; }
    public PricingOutcome Outcome { get; set; }

    /// <summary>Prezzo di riferimento estratto dal marketplace prima degli scostamenti.</summary>
    public decimal? ReferencePrice { get; set; }

    /// <summary>Offerte comparabili rimaste dopo filtri venditore e scarto outlier.</summary>
    public int ComparableOffersCount { get; set; }

    /// <summary>Offerte scartate perché anomale rispetto al gruppo.</summary>
    public int OutliersRejectedCount { get; set; }

    /// <summary>Spiegazione leggibile della decisione, per capire il perché senza rileggere il codice.</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
