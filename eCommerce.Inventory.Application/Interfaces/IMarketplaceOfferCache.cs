using eCommerce.Inventory.Application.DTOs;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Offerte del marketplace Card Trader già lette, per blueprint, riusate per qualche minuto.
///
/// Serve a chi legge lo stesso blueprint più volte a breve distanza: la maschera "Nuovo Prodotto"
/// (statistiche e prezzo proposto, a ogni cambio di condizione, lingua o foil) e il riprezzo
/// subito dopo la pubblicazione, che valuta la carta appena vista nella maschera. Ogni lettura
/// costa una delle 20 richieste al minuto condivise da tutta l'applicazione.
///
/// La notturna non la usa: lì il dato deve essere quello del momento.
/// </summary>
public interface IMarketplaceOfferCache
{
    /// <summary>
    /// Restituisce le offerte lette da non più di <paramref name="maxAge"/>, altrimenti le legge con
    /// <paramref name="fetch"/> e le conserva. Due richieste contemporanee per lo stesso blueprint
    /// condividono la stessa lettura. Una lettura vuota non viene conservata: il client la restituisce
    /// anche quando la chiamata fallisce.
    /// </summary>
    /// <param name="cardTraderBlueprintId">Id del blueprint su Card Trader, non quello locale.</param>
    Task<IReadOnlyList<CardTraderMarketplaceProductDto>> GetOrFetchAsync(
        int cardTraderBlueprintId,
        TimeSpan maxAge,
        Func<CancellationToken, Task<IEnumerable<CardTraderMarketplaceProductDto>>> fetch,
        CancellationToken cancellationToken = default);
}
