using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        ApplicationDbContext dbContext,
        ILogger<InventoryController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get inventory items with pagination
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetInventory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbContext.InventoryItems
                .Include(i => i.Blueprint)
                .ThenInclude(b => b.Expansion)
                .ThenInclude(e => e.Game)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(i =>
                    i.Blueprint.Name.Contains(search) ||
                    i.Blueprint.Expansion.Name.Contains(search));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(i => i.DateAdded)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(new PagedResponse<InventoryItem>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching inventory items");
            return StatusCode(500, new { error = "Failed to fetch inventory items" });
        }
    }

    /// <summary>
    /// Create a new inventory item
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateInventoryItem(
        [FromBody] CreateInventoryItemDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify blueprint exists
            var blueprint = await _dbContext.Blueprints
                .FindAsync(new object[] { dto.BlueprintId }, cancellationToken);

            if (blueprint == null)
            {
                return BadRequest(new { error = "Blueprint not found" });
            }

            var item = new InventoryItem
            {
                BlueprintId = dto.BlueprintId,
                Quantity = dto.Quantity,
                ListingPrice = dto.Price,
                Condition = dto.Condition,
                Language = dto.Language,
                IsFoil = dto.IsFoil,
                IsSigned = dto.IsSigned,
                Location = dto.Location ?? string.Empty,
                PurchasePrice = dto.PurchasePrice,
                DateAdded = DateTime.UtcNow
            };

            _dbContext.InventoryItems.Add(item);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(nameof(GetInventoryItem), new { id = item.Id }, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating inventory item");
            return StatusCode(500, new { error = "Failed to create inventory item" });
        }
    }

    /// <summary>
    /// Get a single inventory item by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetInventoryItem(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _dbContext.InventoryItems
                .Include(i => i.Blueprint)
                .ThenInclude(b => b.Expansion)
                .ThenInclude(e => e.Game)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

            if (item == null)
            {
                return NotFound(new { error = "Inventory item not found" });
            }

            return Ok(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching inventory item {ItemId}", id);
            return StatusCode(500, new { error = "Failed to fetch inventory item" });
        }
    }
}
