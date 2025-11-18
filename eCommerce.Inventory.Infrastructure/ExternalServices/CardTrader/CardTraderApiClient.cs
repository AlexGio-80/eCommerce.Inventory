using System.Net.Http.Json;
using System.Text.Json;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;

public class CardTraderApiClient : ICardTraderApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CardTraderApiClient> _logger;
    private readonly IApplicationDbContext _dbContext;

    public CardTraderApiClient(
        HttpClient httpClient,
        ILogger<CardTraderApiClient> logger,
        IApplicationDbContext dbContext)
    {
        _httpClient = httpClient;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Sync all games from Card Trader API (returns DTOs for mapping)
    /// </summary>
    public async Task<IEnumerable<dynamic>> SyncGamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching games from Card Trader API");

            var response = await _httpClient.GetAsync("games", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<CardTraderGameDto>>(jsonContent)
                ?? new List<CardTraderGameDto>();

            _logger.LogInformation("Fetched {GameCount} games from Card Trader API", dtos.Count);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching games from Card Trader API");
            throw;
        }
    }

    /// <summary>
    /// Sync all expansions from Card Trader API (returns DTOs for mapping)
    /// </summary>
    public async Task<IEnumerable<dynamic>> SyncExpansionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching expansions from Card Trader API");

            var response = await _httpClient.GetAsync("expansions", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<CardTraderExpansionDto>>(jsonContent)
                ?? new List<CardTraderExpansionDto>();

            _logger.LogInformation("Fetched {ExpansionCount} expansions from Card Trader API", dtos.Count);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching expansions from Card Trader API");
            throw;
        }
    }

    /// <summary>
    /// Sync blueprints for a specific expansion from Card Trader API (returns DTOs for mapping)
    /// </summary>
    public async Task<IEnumerable<dynamic>> SyncBlueprintsForExpansionAsync(int expansionId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching blueprints for expansion {ExpansionId} from Card Trader API", expansionId);

            var response = await _httpClient.GetAsync($"expansions/{expansionId}/cards", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<CardTraderBlueprintDto>>(jsonContent)
                ?? new List<CardTraderBlueprintDto>();

            _logger.LogInformation("Fetched {BlueprintCount} blueprints for expansion {ExpansionId} from Card Trader API", dtos.Count, expansionId);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprints for expansion {ExpansionId} from Card Trader API", expansionId);
            throw;
        }
    }

    /// <summary>
    /// Create a new product on Card Trader and return the product ID
    /// </summary>
    public async Task<int> CreateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating product on Card Trader for inventory item {ItemId}", item.Id);

            var payload = new
            {
                blueprint_id = item.BlueprintId,
                price = item.ListingPrice,
                quantity = item.Quantity,
                condition = item.Condition,
                language = item.Language,
                foil = item.IsFoil,
                signed = item.IsSigned,
                user_data_field = item.Location
            };

            var response = await _httpClient.PostAsJsonAsync("products", payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Placeholder: Extract product ID from response
            var productId = 0; // TODO: Parse from response
            _logger.LogInformation("Created product {ProductId} on Card Trader for inventory item {ItemId}", productId, item.Id);
            return productId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product on Card Trader for inventory item {ItemId}", item.Id);
            throw;
        }
    }

    /// <summary>
    /// Update an existing product on Card Trader
    /// </summary>
    public async Task UpdateProductOnCardTraderAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!item.CardTraderProductId.HasValue)
            {
                _logger.LogWarning("Inventory item {ItemId} has no Card Trader product ID", item.Id);
                return;
            }

            _logger.LogInformation("Updating product {ProductId} on Card Trader for inventory item {ItemId}", item.CardTraderProductId, item.Id);

            var payload = new
            {
                price = item.ListingPrice,
                quantity = item.Quantity,
                condition = item.Condition,
                language = item.Language,
                foil = item.IsFoil,
                signed = item.IsSigned,
                user_data_field = item.Location
            };

            var response = await _httpClient.PutAsJsonAsync($"products/{item.CardTraderProductId}", payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Updated product {ProductId} on Card Trader for inventory item {ItemId}", item.CardTraderProductId, item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product {ProductId} on Card Trader for inventory item {ItemId}", item.CardTraderProductId, item.Id);
            throw;
        }
    }

    /// <summary>
    /// Delete a product from Card Trader
    /// </summary>
    public async Task DeleteProductOnCardTraderAsync(int cardTraderProductId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting product {ProductId} from Card Trader", cardTraderProductId);

            var response = await _httpClient.DeleteAsync($"products/{cardTraderProductId}", cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Deleted product {ProductId} from Card Trader", cardTraderProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting product {ProductId} from Card Trader", cardTraderProductId);
            throw;
        }
    }

    /// <summary>
    /// Fetch all my products from Card Trader API (returns DTOs for mapping)
    /// </summary>
    public async Task<List<dynamic>> FetchMyProductsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching my products from Card Trader API");

            var response = await _httpClient.GetAsync("products", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<CardTraderProductDto>>(jsonContent)
                ?? new List<CardTraderProductDto>();

            _logger.LogInformation("Fetched {ProductCount} products from Card Trader API", dtos.Count);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products from Card Trader API");
            throw;
        }
    }

    /// <summary>
    /// Fetch all new orders from Card Trader API (returns DTOs for mapping)
    /// </summary>
    public async Task<List<dynamic>> FetchNewOrdersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching orders from Card Trader API");

            var response = await _httpClient.GetAsync("orders", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<CardTraderOrderDto>>(jsonContent)
                ?? new List<CardTraderOrderDto>();

            _logger.LogInformation("Fetched {OrderCount} orders from Card Trader API", dtos.Count);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders from Card Trader API");
            throw;
        }
    }
}
