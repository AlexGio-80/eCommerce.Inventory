namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Valore atteso dell'apertura di un prodotto sigillato in un giorno (Fase 3 dell'analisi acquisti).
///
/// Calcolato ogni giorno per tutte le uscite con i dati delle buste, dopo l'import del listino: la
/// pagina mostra la classifica senza ricalcolare centinaia di uscite, e le righe dei giorni
/// precedenti dicono come si muove il valore atteso nel tempo, cioè quando conviene comprare.
/// </summary>
public class SealedOpportunity
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    public int SealedProductId { get; set; }
    public SealedProduct? SealedProduct { get; set; }

    /// <summary>Uscita a cui il prodotto appartiene (set principale, es. TRK anche per un prodotto TRC).</summary>
    public string MainSetCode { get; set; } = string.Empty;

    public decimal? CmTrend { get; set; }
    public decimal? CmLow { get; set; }

    /// <summary>Valore atteso netto aprendolo, su prezzi Cardmarket corretti.</summary>
    public decimal? OpenValueCm { get; set; }
    public decimal? CoverageCm { get; set; }

    /// <summary>Ricavato netto rivendendolo chiuso al trend.</summary>
    public decimal? SealedNetCm { get; set; }

    public decimal? OpeningRoiPercent { get; set; }
    public string? Decision { get; set; }

    /// <summary>
    /// Punteggio "da tenere" 0–100 (feature 005): quanto conviene comprarlo per tenerlo uno-tre anni.
    /// Null per i prodotti non valutabili (buste singole, prezzo dubbio, dati incompleti...).
    /// </summary>
    public decimal? HoldScore { get; set; }

    /// <summary>Valore atteso netto dell'apertura / prezzo d'acquisto × 100: il "pavimento".</summary>
    public decimal? FloorPercent { get; set; }

    /// <summary>Mesi dall'uscita del set principale; null per i Secret Lair.</summary>
    public int? ReleaseAgeMonths { get; set; }

    /// <summary>100 = trend al minimo dello storico del prodotto; null finché lo storico è corto.</summary>
    public decimal? HistoryPosition { get; set; }

    /// <summary>Candidato da tenere: punteggio, pavimento ed età entro le soglie <c>Purchasing:Hold:*</c>.</summary>
    public bool IsHoldCandidate { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
