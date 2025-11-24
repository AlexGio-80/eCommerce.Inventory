using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Application.DTOs;
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

            // Deserialize into wrapper DTO to extract array
            var responseWrapper = JsonSerializer.Deserialize<CardTraderGamesResponseDto>(jsonContent);
            var dtos = responseWrapper?.Array ?? new List<CardTraderGameDto>();

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
    /// Returns empty list if expansion has no blueprints or API returns 404
    /// </summary>
    public async Task<IEnumerable<dynamic>> SyncBlueprintsForExpansionAsync(int expansionId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching blueprints for expansion {ExpansionId} from Card Trader API", expansionId);

            // Correct endpoint for fetching blueprints is /blueprints/export?expansion_id={id}
            var response = await _httpClient.GetAsync($"blueprints/export?expansion_id={expansionId}", cancellationToken);

            // Handle 404 gracefully - expansion might not have blueprints or endpoint might not exist
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("No blueprints found for expansion {ExpansionId} (HTTP 404) - continuing with next expansion", expansionId);
                return new List<dynamic>();
            }

            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Handle empty array responses (some expansions have no blueprints)
            if (string.IsNullOrWhiteSpace(jsonContent) || jsonContent.Trim() == "[]")
            {
                _logger.LogInformation("Expansion {ExpansionId} has no blueprints (empty response)", expansionId);
                return new List<dynamic>();
            }

            var dtos = JsonSerializer.Deserialize<List<CardTraderBlueprintDto>>(jsonContent)
                ?? new List<CardTraderBlueprintDto>();

            _logger.LogInformation("Fetched {BlueprintCount} blueprints for expansion {ExpansionId} from Card Trader API", dtos.Count, expansionId);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprints for expansion {ExpansionId} from Card Trader API", expansionId);
            // Return empty list instead of throwing - don't block other expansions
            return new List<dynamic>();
        }
    }

    /// <summary>
    /// Sync all categories from Card Trader API (returns DTOs for mapping)
    /// Categories define product attributes and their possible values for each game
    /// </summary>
    public async Task<IEnumerable<dynamic>> SyncCategoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching categories from Card Trader API");

            var response = await _httpClient.GetAsync("categories", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Log the actual JSON response for debugging
            _logger.LogInformation("Categories API Response (first 500 chars): {Response}",
                jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent);

            List<CardTraderCategoryDto> dtos = new();

            try
            {
                // First try to deserialize as wrapper with array (same pattern as games)
                var responseWrapper = JsonSerializer.Deserialize<CardTraderCategoriesResponseDto>(jsonContent);
                dtos = responseWrapper?.Array ?? new List<CardTraderCategoryDto>();
            }
            catch (JsonException)
            {
                // If wrapper format fails, try deserializing as direct array
                _logger.LogInformation("Wrapper format failed, trying direct array deserialization");
                dtos = JsonSerializer.Deserialize<List<CardTraderCategoryDto>>(jsonContent)
                    ?? new List<CardTraderCategoryDto>();
            }

            _logger.LogInformation("Fetched {CategoryCount} categories from Card Trader API", dtos.Count);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching categories from Card Trader API");
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
                blueprint_id = item.Blueprint?.CardTraderId ?? throw new InvalidOperationException($"Blueprint not loaded for InventoryItem {item.Id}"),
                price = item.ListingPrice,
                quantity = item.Quantity,
                user_data_field = item.Location,
                tag = item.Tag,
                properties = new Dictionary<string, object>
                {
                    { "condition", item.Condition },
                    { "mtg_language", GetLanguageCode(item.Language) },
                    { "mtg_foil", item.IsFoil },
                    { "signed", item.IsSigned },
                    { "altered", false }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            _logger.LogInformation("Sending Create Product Payload: {Payload}", jsonPayload);

            var response = await _httpClient.PostAsJsonAsync("products", payload, cancellationToken);
            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create product on Card Trader. Status: {StatusCode}, Response: {Response}",
                    response.StatusCode, jsonResponse);
                response.EnsureSuccessStatusCode(); // Will throw
            }

            using var doc = JsonDocument.Parse(jsonResponse);

            // Card Trader API returns the product in a 'resource' wrapper (or directly for some endpoints)
            JsonElement productElement = doc.RootElement;
            if (doc.RootElement.TryGetProperty("resource", out var resourceElement))
            {
                productElement = resourceElement;
            }

            if (productElement.TryGetProperty("id", out var idElement))
            {
                var productId = idElement.GetInt32();
                _logger.LogInformation("Created product {ProductId} on Card Trader for inventory item {ItemId}", productId, item.Id);
                return productId;
            }

            throw new Exception($"Failed to parse product ID from response: {jsonResponse}");
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
    /// Fetch all products via Export endpoint (for full sync)
    /// Endpoint: /api/v2/products/export
    /// </summary>
    public async Task<List<dynamic>> GetProductsExportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching products export from Card Trader API");

            var response = await _httpClient.GetAsync("products/export", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var dtos = JsonSerializer.Deserialize<List<CardTraderProductDto>>(jsonContent)
                ?? new List<CardTraderProductDto>();

            _logger.LogInformation("Fetched {ProductCount} products from Export API", dtos.Count);
            return dtos.Cast<dynamic>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products export from Card Trader API");
            throw;
        }
    }

    /// <summary>
    /// Fetch all orders from Card Trader API (returns DTOs for mapping)
    /// </summary>
    public async Task<IEnumerable<dynamic>> GetOrdersAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching orders from Card Trader API (from: {From}, to: {To})", from, to);

            // Build query string with date filters
            var queryParams = new List<string>
            {
                "sort=date.desc",
                "limit=1000"  // Increase limit to get more orders per request
            };

            if (from.HasValue)
            {
                queryParams.Add($"from={from.Value:yyyy-MM-dd}");
            }
            if (to.HasValue)
            {
                queryParams.Add($"to={to.Value:yyyy-MM-dd}");
            }

            var queryString = string.Join("&", queryParams);
            var endpoint = $"orders?{queryString}";

            _logger.LogInformation("Calling Card Trader endpoint: {Endpoint}", endpoint);

            var response = await _httpClient.GetAsync(endpoint, cancellationToken);
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

    /// <summary>
    /// Convert language name to Card Trader language code (e.g., "English" -> "en")
    /// </summary>
    private static string GetLanguageCode(string languageName)
    {
        return languageName?.ToLowerInvariant() switch
        {
            "english" => "en",
            "french" => "fr",
            "german" => "de",
            "spanish" => "es",
            "italian" => "it",
            "portuguese" => "pt",
            "japanese" => "ja",
            "chinese" => "zh",
            "russian" => "ru",
            "korean" => "ko",
            _ => "en" // Default to English
        };
    }

    /// <summary>
    /// Fetch marketplace products for a specific blueprint
    /// Endpoint: /api/v2/marketplace/products?blueprint_id={id}
    /// </summary>
    public async Task<IEnumerable<CardTraderMarketplaceProductDto>> GetMarketplaceProductsAsync(int blueprintId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching marketplace products for blueprint {BlueprintId} from Card Trader API", blueprintId);

            var response = await _httpClient.GetAsync($"marketplace/products?blueprint_id={blueprintId}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // The endpoint returns a dictionary where keys are Blueprint IDs (strings) and values are LISTS of product objects
            var productsDict = JsonSerializer.Deserialize<Dictionary<string, List<CardTraderMarketplaceProductDto>>>(jsonContent);

            var products = productsDict?.Values.SelectMany(x => x).ToList() ?? new List<CardTraderMarketplaceProductDto>();

            _logger.LogInformation("Fetched {ProductCount} marketplace products for blueprint {BlueprintId}", products.Count, blueprintId);
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching marketplace products for blueprint {BlueprintId}", blueprintId);
            return new List<CardTraderMarketplaceProductDto>();
        }
    }
}
