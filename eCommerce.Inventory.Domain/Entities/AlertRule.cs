namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Regola di avviso sugli acquisti di sigillati (Fase 4 dell'analisi acquisti). Si valuta ogni
/// mattina dopo la classifica delle opportunità; scatta quando la condizione diventa vera per un
/// prodotto, non ogni giorno finché resta vera (lo stato è in <see cref="AlertRuleMatch"/>).
/// </summary>
public class AlertRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AlertRuleType Type { get; set; }

    /// <summary>Prodotto controllato, per <see cref="AlertRuleType.PriceBelow"/> e <see cref="AlertRuleType.PriceDrop"/>.</summary>
    public int? SealedProductId { get; set; }
    public SealedProduct? SealedProduct { get; set; }

    /// <summary>Filtro facoltativo per <see cref="AlertRuleType.OpeningOpportunity"/>: solo questa uscita (set principale).</summary>
    public string? SetCode { get; set; }

    /// <summary>Filtro facoltativo per <see cref="AlertRuleType.OpeningOpportunity"/>: solo questa categoria (es. booster_box).</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Soglia: euro per <see cref="AlertRuleType.PriceBelow"/>, percentuale di calo per
    /// <see cref="AlertRuleType.PriceDrop"/>, resa minima % per <see cref="AlertRuleType.OpeningOpportunity"/>.
    /// </summary>
    public decimal Threshold { get; set; }

    /// <summary>Per <see cref="AlertRuleType.PriceBelow"/>: confronto sul prezzo più basso invece che sul trend.</summary>
    public bool UseLowPrice { get; set; }

    public bool IsActive { get; set; } = true;
    public bool SendEmail { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastEvaluatedAt { get; set; }
}

public enum AlertRuleType
{
    /// <summary>Trend (o low) Cardmarket di un prodotto sotto una soglia in euro.</summary>
    PriceBelow = 0,
    /// <summary>Trend Cardmarket di un prodotto sceso di almeno la soglia % rispetto a 7 giorni prima.</summary>
    PriceDrop = 1,
    /// <summary>Un prodotto entra in "Apri" con resa dell'apertura almeno pari alla soglia %.</summary>
    OpeningOpportunity = 2
}

/// <summary>
/// Prodotto per cui una regola è vera dall'ultima valutazione. Serve a far scattare l'avviso solo
/// quando la condizione diventa vera: quando smette di esserlo la riga si cancella, e se torna vera
/// l'avviso scatta di nuovo.
/// </summary>
public class AlertRuleMatch
{
    public int Id { get; set; }
    public int AlertRuleId { get; set; }
    public AlertRule? AlertRule { get; set; }
    public int SealedProductId { get; set; }
    public DateTime MatchingSince { get; set; } = DateTime.UtcNow;
}

/// <summary>Avviso emesso, mostrato nella campanella dell'app ed eventualmente inviato via email.</summary>
public class AlertNotification
{
    public int Id { get; set; }

    /// <summary>Regola che l'ha generato; null se la regola è stata cancellata (l'avviso resta).</summary>
    public int? AlertRuleId { get; set; }
    public AlertRule? AlertRule { get; set; }

    public int? SealedProductId { get; set; }

    /// <summary>Uscita del prodotto, per aprirne l'analisi dalla campanella.</summary>
    public string? SetCode { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    public bool EmailRequested { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public string? EmailError { get; set; }
}
