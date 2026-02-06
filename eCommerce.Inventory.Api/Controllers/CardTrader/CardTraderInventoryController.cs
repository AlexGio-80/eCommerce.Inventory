using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Api.Controllers.CardTrader;

[ApiController]
[Route("api/cardtrader/inventory")]
public class CardTraderInventoryController : ControllerBase
{
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly ICardTraderApiService _cardTraderApiService;
    private readonly ILogger<CardTraderInventoryController> _logger;

    public CardTraderInventoryController(
        IInventoryItemRepository inventoryItemRepository,
        ICardTraderApiService cardTraderApiService,
        ILogger<CardTraderInventoryController> logger)
    {
        _inventoryItemRepository = inventoryItemRepository;
        _cardTraderApiService = cardTraderApiService;
        _logger = logger;
    }

    /// <summary>
    /// Get inventory items for Card Trader with pagination and filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<Models.ApiResponse<Models.PagedResponse<InventoryItem>>>> GetInventoryItems(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? cardName = null,
        [FromQuery] string? expansionName = null,
        [FromQuery] string? condition = null,
        [FromQuery] string? language = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting inventory items - page: {Page}, pageSize: {PageSize}, searchTerm: {SearchTerm}",
            page, pageSize, searchTerm);

        // Use repository for simple case (no filters)
        if (string.IsNullOrWhiteSpace(searchTerm) &&
            string.IsNullOrWhiteSpace(cardName) &&
            string.IsNullOrWhiteSpace(expansionName) &&
            string.IsNullOrWhiteSpace(condition) &&
            string.IsNullOrWhiteSpace(language))
        {
            var (items, totalCount) = await _inventoryItemRepository.GetPagedAsync(page, pageSize, cancellationToken);
            var pagedData = Models.PagedResponse<InventoryItem>.Create(
                items: items.ToList(),
                page: page,
                pageSize: pageSize,
                totalCount: totalCount
            );
            return Ok(Models.ApiResponse<Models.PagedResponse<InventoryItem>>.SuccessResult(pagedData));
        }

        // For filtered queries, we need to access DbContext directly
        // This is acceptable for complex query scenarios
        var dbContext = _inventoryItemRepository as Infrastructure.Persistence.Repositories.InventoryItemRepository;
        if (dbContext == null)
        {
            // Fallback: use repository without filters
            var (items, totalCount) = await _inventoryItemRepository.GetPagedAsync(page, pageSize, cancellationToken);
            var pagedData = Models.PagedResponse<InventoryItem>.Create(
                items: items.ToList(),
                page: page,
                pageSize: pageSize,
                totalCount: totalCount
            );
            return Ok(Models.ApiResponse<Models.PagedResponse<InventoryItem>>.SuccessResult(pagedData));
        }

        // Build filtered query using DbContext
        var query = dbContext.GetFilteredQuery(searchTerm, cardName, expansionName, condition, language);

        // Get total count
        var filteredTotalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var filteredItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var filteredPagedData = Models.PagedResponse<InventoryItem>.Create(
            items: filteredItems,
            page: page,
            pageSize: pageSize,
            totalCount: filteredTotalCount
        );

        return Ok(Models.ApiResponse<Models.PagedResponse<InventoryItem>>.SuccessResult(filteredPagedData));
    }

    /// <summary>
    /// Get a specific inventory item by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Models.ApiResponse<InventoryItem>>> GetInventoryItemById(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting inventory item {ItemId} for Card Trader", id);
        var item = await _inventoryItemRepository.GetByIdAsync(id, cancellationToken);

        if (item == null)
        {
            _logger.LogWarning("Inventory item {ItemId} not found", id);
            return NotFound(Models.ApiResponse<InventoryItem>.ErrorResult($"Inventory item with ID {id} not found"));
        }

        return Ok(Models.ApiResponse<InventoryItem>.SuccessResult(item));
    }

    /// <summary>
    /// Add a new inventory item to Card Trader
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Models.ApiResponse<InventoryItem>>> AddInventoryItem(
        [FromBody] CreateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding new inventory item to Card Trader");

        var item = new InventoryItem
        {
            BlueprintId = request.BlueprintId,
            PurchasePrice = request.PurchasePrice,
            Quantity = request.Quantity,
            ListingPrice = request.ListingPrice,
            Condition = request.Condition,
            Language = request.Language,
            IsFoil = request.IsFoil,
            IsSigned = request.IsSigned,
            IsAltered = request.IsAltered,
            Location = request.Location,
            DateAdded = DateTime.UtcNow
        };

        await _inventoryItemRepository.AddAsync(item, cancellationToken);
        _logger.LogInformation("Inventory item {ItemId} added successfully", item.Id);

        var response = Models.ApiResponse<InventoryItem>.SuccessResult(item, "Inventory item created successfully");
        return CreatedAtAction(nameof(GetInventoryItemById), new { id = item.Id }, response);
    }

    /// <summary>
    /// Update an existing inventory item on Card Trader
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Models.ApiResponse<InventoryItem>>> UpdateInventoryItem(
        int id,
        [FromBody] UpdateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating inventory item {ItemId} on Card Trader", id);

        var item = await _inventoryItemRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
        {
            _logger.LogWarning("Inventory item {ItemId} not found for update", id);
            return NotFound(Models.ApiResponse<InventoryItem>.ErrorResult($"Inventory item with ID {id} not found"));
        }

        // Update only the provided fields
        if (request.ListingPrice.HasValue)
            item.ListingPrice = request.ListingPrice.Value;

        if (request.Quantity.HasValue)
            item.Quantity = request.Quantity.Value;

        if (!string.IsNullOrEmpty(request.Condition))
            item.Condition = request.Condition;

        if (!string.IsNullOrEmpty(request.Language))
            item.Language = request.Language;

        if (!string.IsNullOrEmpty(request.Tag))
            item.Tag = request.Tag;

        if (request.IsFoil.HasValue)
            item.IsFoil = request.IsFoil.Value;

        if (request.IsSigned.HasValue)
            item.IsSigned = request.IsSigned.Value;

        if (request.IsAltered.HasValue)
            item.IsAltered = request.IsAltered.Value;

        if (!string.IsNullOrEmpty(request.Location))
            item.Location = request.Location;

        await _inventoryItemRepository.UpdateAsync(item, cancellationToken);
        _logger.LogInformation("Inventory item {ItemId} updated successfully in local database", id);

        // Sync to Card Trader if the item has a CardTraderProductId
        if (item.CardTraderProductId.HasValue)
        {
            try
            {
                await _cardTraderApiService.UpdateProductOnCardTraderAsync(item, cancellationToken);
                _logger.LogInformation("Inventory item {ItemId} synced to Card Trader successfully", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync inventory item {ItemId} to Card Trader", id);
                // Continue anyway - local update succeeded
            }
        }
        else
        {
            _logger.LogWarning("Inventory item {ItemId} does not have a CardTraderProductId, skipping Card Trader sync", id);
        }

        return Ok(Models.ApiResponse<InventoryItem>.SuccessResult(item, "Inventory item updated successfully"));
    }

    /// <summary>
    /// Delete an inventory item from Card Trader
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<Models.ApiResponse<object>>> DeleteInventoryItem(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting inventory item {ItemId} from Card Trader", id);

        var item = await _inventoryItemRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
        {
            _logger.LogWarning("Inventory item {ItemId} not found for deletion", id);
            return NotFound(Models.ApiResponse<object>.ErrorResult($"Inventory item with ID {id} not found"));
        }

        await _inventoryItemRepository.DeleteAsync(id, cancellationToken);
        _logger.LogInformation("Inventory item {ItemId} deleted successfully", id);

        return Ok(Models.ApiResponse<object>.SuccessResult(null, "Inventory item deleted successfully"));
    }
    /// <summary>
    /// Get marketplace statistics for a blueprint (Min, Max, Avg price)
    /// </summary>
    [HttpGet("marketplace-stats/{blueprintId}")]
    public async Task<ActionResult<Application.DTOs.MarketplaceStatsDto>> GetMarketplaceStats(
        int blueprintId,
        [FromQuery] string? condition = null,
        [FromQuery] string? language = null,
        [FromQuery] bool? isFoil = null,
        [FromQuery] bool? isSigned = null)
    {
        try
        {
            var products = await _cardTraderApiService.GetMarketplaceProductsAsync(blueprintId);

            if (!products.Any())
            {
                return Ok(new Application.DTOs.MarketplaceStatsDto
                {
                    BlueprintId = blueprintId,
                    MinPrice = 0,
                    MaxPrice = 0,
                    AveragePrice = 0,
                    TotalListings = 0
                });
            }

            // Filter products based on parameters
            var query = products.Where(p => p.PriceCents > 0);

            // Apply price cap (1000 EUR = 100,000 cents)
            // Users often upload cards with absurd values (placeholder)
            query = query.Where(p => p.PriceCents <= 100000);

            var filteredProducts = query.ToList();
            _logger.LogInformation("Blueprint {BlueprintId}: {Total} products after price cap filter", blueprintId, filteredProducts.Count);

            if (!string.IsNullOrWhiteSpace(condition))
            {
                // Card Trader uses both full names "Near Mint" and abbreviations "Nm" or "Mint"
                // We'll try to be flexible
                filteredProducts = filteredProducts.Where(p =>
                {
                    var prodCondition = p.Properties.Condition ?? (p.PropertiesHash.TryGetValue("condition", out var c) ? c?.ToString() : null);
                    if (prodCondition == null) return false;

                    if (prodCondition.Equals(condition, StringComparison.OrdinalIgnoreCase)) return true;

                    // Common abbreviations
                    if (condition.Equals("Near Mint", StringComparison.OrdinalIgnoreCase) &&
                        (prodCondition.Equals("Nm", StringComparison.OrdinalIgnoreCase) || prodCondition.Equals("Mint", StringComparison.OrdinalIgnoreCase))) return true;
                    if (condition.Equals("Slightly Played", StringComparison.OrdinalIgnoreCase) && prodCondition.Equals("Sp", StringComparison.OrdinalIgnoreCase)) return true;
                    if (condition.Equals("Moderately Played", StringComparison.OrdinalIgnoreCase) && prodCondition.Equals("Mp", StringComparison.OrdinalIgnoreCase)) return true;
                    if (condition.Equals("Played", StringComparison.OrdinalIgnoreCase) && prodCondition.Equals("Pl", StringComparison.OrdinalIgnoreCase)) return true;
                    if (condition.Equals("Poor", StringComparison.OrdinalIgnoreCase) && prodCondition.Equals("Po", StringComparison.OrdinalIgnoreCase)) return true;

                    return false;
                }).ToList();
                _logger.LogInformation("After condition filter ({Condition}): {Count} products", condition, filteredProducts.Count);
            }

            if (!string.IsNullOrWhiteSpace(language))
            {
                // Card Trader uses both names "English" and codes "en"
                filteredProducts = filteredProducts.Where(p =>
                {
                    var prodLanguage = p.Properties.Language ?? (p.PropertiesHash.TryGetValue("language", out var l) ? l?.ToString() : null);
                    if (prodLanguage == null)
                    {
                        // Some games might use mtg_language
                        prodLanguage = p.PropertiesHash.TryGetValue("mtg_language", out var ml) ? ml?.ToString() : null;
                    }

                    if (prodLanguage == null) return false;

                    if (prodLanguage.Equals(language, StringComparison.OrdinalIgnoreCase)) return true;

                    // Check codes
                    var code = language.ToLowerInvariant() switch
                    {
                        "english" => "en",
                        "italian" => "it",
                        "japanese" => "ja",
                        "french" => "fr",
                        "german" => "de",
                        "spanish" => "es",
                        "chinese" => "zh",
                        _ => null
                    };

                    return code != null && prodLanguage.Equals(code, StringComparison.OrdinalIgnoreCase);
                }).ToList();
                _logger.LogInformation("After language filter ({Language}): {Count} products", language, filteredProducts.Count);
            }

            if (isFoil.HasValue)
            {
                filteredProducts = filteredProducts.Where(p => p.Properties.IsFoil == isFoil.Value).ToList();
                _logger.LogInformation("After foil filter ({IsFoil}): {Count} products", isFoil.Value, filteredProducts.Count);
            }

            if (isSigned.HasValue)
            {
                filteredProducts = filteredProducts.Where(p => p.Properties.IsSigned == isSigned.Value).ToList();
                _logger.LogInformation("After signed filter ({IsSigned}): {Count} products", isSigned.Value, filteredProducts.Count);
            }

            if (!filteredProducts.Any() && products.Any())
            {
                // Log sample properties to help debug if no products matched
                var sample = products.Take(3).Select(p => new
                {
                    Cond = p.Properties.Condition,
                    Lang = p.Properties.Language,
                    Foil = p.Properties.IsFoil,
                    Signed = p.Properties.IsSigned,
                    Hash = p.PropertiesHash
                });
                _logger.LogWarning("Zero listings found for Blueprint {BlueprintId} with filters. Sample products: {@Sample}", blueprintId, sample);
            }

            var validProducts = filteredProducts;

            if (!validProducts.Any())
            {
                return Ok(new Application.DTOs.MarketplaceStatsDto
                {
                    BlueprintId = blueprintId,
                    MinPrice = 0,
                    MaxPrice = 0,
                    AveragePrice = 0,
                    TotalListings = 0
                });
            }

            var minPriceCents = validProducts.Min(p => p.PriceCents);
            var maxPriceCents = validProducts.Max(p => p.PriceCents);
            var avgPriceCents = validProducts.Average(p => p.PriceCents);

            return Ok(new Application.DTOs.MarketplaceStatsDto
            {
                BlueprintId = blueprintId,
                MinPrice = minPriceCents / 100m,
                MaxPrice = maxPriceCents / 100m,
                AveragePrice = Math.Round((decimal)avgPriceCents / 100m, 2),
                TotalListings = validProducts.Count,
                Currency = validProducts.First().PriceCurrency
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting marketplace stats for blueprint {BlueprintId}", blueprintId);
            return StatusCode(500, "Error getting marketplace stats");
        }
    }
}

public class CreateInventoryItemRequest
{
    public int BlueprintId { get; set; }
    public decimal PurchasePrice { get; set; }
    public int Quantity { get; set; }
    public decimal ListingPrice { get; set; }
    public string Condition { get; set; }
    public string Language { get; set; }
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }
    public bool IsAltered { get; set; }
    public string Location { get; set; }
}

public class UpdateInventoryItemRequest
{
    public decimal? ListingPrice { get; set; }
    public int? Quantity { get; set; }
    public string? Condition { get; set; }
    public string? Language { get; set; }
    public string? Tag { get; set; }
    public bool? IsFoil { get; set; }
    public bool? IsSigned { get; set; }
    public bool? IsAltered { get; set; }
    public string? Location { get; set; }
}
