using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Pricing;

/// <summary>
/// Esito motivato della valutazione di una carta. Porta con sé il perché, non solo il numero:
/// è ciò che permette di tarare le regole guardando un report invece di leggere il codice.
/// </summary>
public class PricingDecision
{
    public decimal OldPrice { get; set; }
    public decimal ProposedPrice { get; set; }
    public decimal? ReferencePrice { get; set; }

    public PricingOutcome Outcome { get; set; }
    public string Reason { get; set; } = string.Empty;

    public int ComparableOffersCount { get; set; }
    public int OutliersRejectedCount { get; set; }

    public int? RuleId { get; set; }
    public PricingRule? Rule { get; set; }

    /// <summary>Vero solo quando il prezzo va effettivamente scritto su Card Trader.</summary>
    public bool ShouldWrite => Outcome == PricingOutcome.Applied;

    /// <summary>Variazione proposta, con segno.</summary>
    public decimal Delta => ProposedPrice - OldPrice;

    public static PricingDecision Skip(
        PricingOutcome outcome,
        decimal currentPrice,
        string reason,
        int comparableOffers = 0,
        int outliersRejected = 0) => new()
        {
            Outcome = outcome,
            OldPrice = currentPrice,
            ProposedPrice = currentPrice,
            Reason = reason,
            ComparableOffersCount = comparableOffers,
            OutliersRejectedCount = outliersRejected
        };
}
