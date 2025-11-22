using Microsoft.AspNetCore.Mvc;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Api.Controllers.CardTrader;

[ApiController]
[Route("api/cardtrader/inventory")]
public class CardTraderInventoryController : ControllerBase
{
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly ILogger<CardTraderInventoryController> _logger;

    public CardTraderInventoryController(
        IInventoryItemRepository inventoryItemRepository,
        ILogger<CardTraderInventoryController> logger)
    {
        _inventoryItemRepository = inventoryItemRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get inventory items for Card Trader with pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<object>> GetInventoryItems(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting inventory items - page: {Page}, pageSize: {PageSize}", page, pageSize);

        var (items, totalCount) = await _inventoryItemRepository.GetPagedAsync(page, pageSize, cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new
        {
            items,
            totalCount,
            pageNumber = page,
            pageSize,
            totalPages
        });
    }

    /// <summary>
    /// Get a specific inventory item by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<InventoryItem>> GetInventoryItemById(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting inventory item {ItemId} for Card Trader", id);
        var item = await _inventoryItemRepository.GetByIdAsync(id, cancellationToken);

        if (item == null)
        {
            _logger.LogWarning("Inventory item {ItemId} not found", id);
            return NotFound(new { message = $"Inventory item with ID {id} not found" });
        }

        return Ok(item);
    }

    /// <summary>
    /// Add a new inventory item to Card Trader
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<InventoryItem>> AddInventoryItem(
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
            Location = request.Location,
            DateAdded = DateTime.UtcNow
        };

        await _inventoryItemRepository.AddAsync(item, cancellationToken);
        _logger.LogInformation("Inventory item {ItemId} added successfully", item.Id);

        return CreatedAtAction(nameof(GetInventoryItemById), new { id = item.Id }, item);
    }

    /// <summary>
    /// Update an existing inventory item on Card Trader
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateInventoryItem(
        int id,
        [FromBody] UpdateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating inventory item {ItemId} on Card Trader", id);

        var item = await _inventoryItemRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
        {
            _logger.LogWarning("Inventory item {ItemId} not found for update", id);
            return NotFound(new { message = $"Inventory item with ID {id} not found" });
        }

        // Update only the provided fields
        if (request.ListingPrice.HasValue)
            item.ListingPrice = request.ListingPrice.Value;

        if (request.Quantity.HasValue)
            item.Quantity = request.Quantity.Value;

        if (!string.IsNullOrEmpty(request.Condition))
            item.Condition = request.Condition;

        if (!string.IsNullOrEmpty(request.Location))
            item.Location = request.Location;

        await _inventoryItemRepository.UpdateAsync(item, cancellationToken);
        _logger.LogInformation("Inventory item {ItemId} updated successfully", id);

        return Ok(item);
    }

    /// <summary>
    /// Delete an inventory item from Card Trader
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInventoryItem(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting inventory item {ItemId} from Card Trader", id);

        var item = await _inventoryItemRepository.GetByIdAsync(id, cancellationToken);
        if (item == null)
        {
            _logger.LogWarning("Inventory item {ItemId} not found for deletion", id);
            return NotFound(new { message = $"Inventory item with ID {id} not found" });
        }

        await _inventoryItemRepository.DeleteAsync(id, cancellationToken);
        _logger.LogInformation("Inventory item {ItemId} deleted successfully", id);

        return NoContent();
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
    public string Location { get; set; }
}

public class UpdateInventoryItemRequest
{
    public decimal? ListingPrice { get; set; }
    public int? Quantity { get; set; }
    public string Condition { get; set; }
    public string Location { get; set; }
}
