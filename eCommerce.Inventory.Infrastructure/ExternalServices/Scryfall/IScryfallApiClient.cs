using eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall.DTOs;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall;

public interface IScryfallApiClient
{
    Task<IEnumerable<ScryfallSetDto>> GetSetsAsync(CancellationToken cancellationToken = default);
}
