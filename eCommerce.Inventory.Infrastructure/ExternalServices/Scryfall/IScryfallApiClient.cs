using eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall.DTOs;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall;

public interface IScryfallApiClient
{
    Task<IEnumerable<ScryfallSetDto>> GetSetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a card by its Scryfall ID
    /// </summary>
    Task<ScryfallCardDto?> GetCardByIdAsync(string scryfallId, CancellationToken cancellationToken = default);
}
