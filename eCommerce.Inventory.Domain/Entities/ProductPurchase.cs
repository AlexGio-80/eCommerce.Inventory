namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Acquisto di un prodotto sigillato (registro della Fase 5 dell'analisi acquisti).
///
/// Oltre a ricordare cosa si è comprato, a quanto e dove, salva la previsione del modello al
/// momento dell'apertura: fra qualche mese il confronto fra previsione e vendite reali (tramite
/// <see cref="Tag"/>, lo stesso delle inserzioni) dice quanto il modello va corretto.
/// </summary>
public class ProductPurchase
{
    public int Id { get; set; }

    public int SealedProductId { get; set; }
    public SealedProduct? SealedProduct { get; set; }

    public int Quantity { get; set; }

    /// <summary>Prezzo pagato per un'unità, spedizione compresa se la si vuole conteggiare.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Dove si è comprato (es. "Cardmarket", "Wizards").</summary>
    public string? Store { get; set; }

    public string? Seller { get; set; }

    public DateOnly? PurchasedAt { get; set; }

    /// <summary>Data di apertura; null se ancora sigillato.</summary>
    public DateOnly? OpenedAt { get; set; }

    /// <summary>Tag delle inserzioni delle carte aperte, formato <c>CODICE_TIPO_AAAAMMGG</c>.</summary>
    public string? Tag { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Valore atteso netto di un'unità aprendola (prezzi Cardmarket corretti), calcolato al momento
    /// dell'apertura o, se ancora chiusa, della registrazione. Null se i dati non bastavano (es.
    /// composizione delle buste non ancora pubblicata): si ricalcola all'apertura.
    /// </summary>
    public decimal? PredictedOpenValueNet { get; set; }

    /// <summary>Copertura dei prezzi della previsione (0-100).</summary>
    public decimal? PredictionCoverage { get; set; }

    public DateTime? PredictedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
