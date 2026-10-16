namespace eCommerce.Inventory.Application.DTOs;

/// <summary>
/// Prezzo proposto dalle regole dell'autopricer per una carta da mettere in vendita dalla maschera
/// "Nuovo Prodotto". <see cref="SuggestedPrice"/> è null quando le regole non danno un prezzo
/// (mercato insufficiente, nessuna regola, nessun profilo attivo): <see cref="Reason"/> dice perché.
/// </summary>
public class ListingPriceSuggestionDto
{
    public decimal? SuggestedPrice { get; set; }

    /// <summary>Esito del motore (<c>PricingOutcome</c>), o "NoActiveProfile".</summary>
    public string Outcome { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int ComparableOffersCount { get; set; }

    /// <summary>Prezzo in vetrina dell'offerta presa a riferimento.</summary>
    public decimal? ReferencePrice { get; set; }

    public string? ProfileName { get; set; }
}
