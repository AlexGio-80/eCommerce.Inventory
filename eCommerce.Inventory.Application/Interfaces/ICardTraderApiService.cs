using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Application.DTOs;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Interface for Card Trader API service
/// Defines contract for fetching and managing data from Card Trader
/// Following SPECIFICATIONS: Dependency Inversion (depend on abstractions)
///
/// Design Note: Returns dynamic objects to support Separation of Concerns
/// - CardTraderApiService: Fetches raw data from API → returns DTOs (as dynamic)
/// - CardTraderDtoMapper: Converts DTOs → Domain Entities
/// - InventorySyncService: Persists entities → saves to database
/// </summary>
public interface ICardTraderApiService
{
    // Fetch methods - return DTOs (as dynamic) for mapping layer to handle
    Task<IEnumerable<dynamic>> SyncGamesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<dynamic>> SyncExpansionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<dynamic>> SyncBlueprintsForExpansionAsync(int expansionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<dynamic>> SyncCategoriesAsync(CancellationToken cancellationToken = default);
    Task<List<dynamic>> FetchMyProductsAsync(CancellationToken cancellationToken = default);
    Task<List<dynamic>> GetProductsExportAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<dynamic>> GetOrdersAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<CardTraderMarketplaceProductDto>> GetMarketplaceProductsAsync(int blueprintId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CardTraderMarketplaceProductDto>> GetMarketplaceProductsBatchAsync(IEnumerable<int> blueprintIds, CancellationToken cancellationToken = default);

    // Mutation methods - operate on Card Trader marketplace
    Task<int> CreateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task UpdateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task DeleteProductOnCardTraderAsync(int cardTraderProductId, CancellationToken cancellationToken = default);
}
