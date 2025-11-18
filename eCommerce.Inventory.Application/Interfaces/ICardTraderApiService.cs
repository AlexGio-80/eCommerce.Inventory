using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

public interface ICardTraderApiService
{
    Task<IEnumerable<Game>> SyncGamesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Expansion>> SyncExpansionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Blueprint>> SyncBlueprintsForExpansionAsync(int expansionId, CancellationToken cancellationToken = default);
    Task<int> CreateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task UpdateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task DeleteProductOnCardTraderAsync(int cardTraderProductId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> FetchNewOrdersAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<InventoryItem>> FetchMyProductsAsync(CancellationToken cancellationToken = default);
}
