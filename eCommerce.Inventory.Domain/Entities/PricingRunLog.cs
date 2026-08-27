namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Riepilogo di una esecuzione dell'autopricer. Serve a rispondere con un dato,
/// e non a memoria, alla domanda "ha aggiornato davvero tutto quello che doveva?".
/// </summary>
public class PricingRunLog
{
    public int Id { get; set; }

    public int PricingProfileId { get; set; }
    public PricingProfile? PricingProfile { get; set; }

    public PricingTrigger Trigger { get; set; }
    public bool DryRun { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Blueprint che l'esecuzione si era prefissata di valutare.</summary>
    public int PlannedCount { get; set; }

    public int EvaluatedCount { get; set; }
    public int AppliedCount { get; set; }
    public int SimulatedCount { get; set; }
    public int NoChangeCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>Somma algebrica delle variazioni applicate, per vedere a colpo d'occhio la direzione.</summary>
    public decimal TotalPriceDelta { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Percentuale di copertura sul pianificato: è la metrica che l'autopricer nativo non dà.</summary>
    public decimal CoveragePercent => PlannedCount == 0
        ? 0
        : Math.Round((decimal)EvaluatedCount / PlannedCount * 100m, 2);

    public ICollection<PriceChangeLog> Changes { get; set; } = new List<PriceChangeLog>();
}
