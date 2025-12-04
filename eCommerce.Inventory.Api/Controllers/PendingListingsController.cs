using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Api.Controllers;

[ApiController]
[Route("api/pending-listings")]
public class PendingListingsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICardTraderApiService _cardTraderService;
    private readonly ILogger<PendingListingsController> _logger;

    public PendingListingsController(
        ApplicationDbContext dbContext,
        ICardTraderApiService cardTraderService,
        ILogger<PendingListingsController> logger)
    {
        _dbContext = dbContext;
        _cardTraderService = cardTraderService;
        _logger = logger;
    }

    /// <summary>
    /// Get pending listings with filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<Models.ApiResponse<PagedResponse<PendingListing>>>> GetPendingListings(
        [FromQuery] bool? isSynced = null,
        [FromQuery] bool hasError = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PendingListings
            .Include(p => p.Blueprint)
            .ThenInclude(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .AsNoTracking()
            .AsQueryable();

        if (isSynced.HasValue)
        {
            query = query.Where(p => p.IsSynced == isSynced.Value);
        }

        if (hasError)
        {
            query = query.Where(p => p.SyncError != null);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var pagedResponse = new PagedResponse<PendingListing>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return Ok(Models.ApiResponse<PagedResponse<PendingListing>>.SuccessResult(pagedResponse));
    }

    /// <summary>
    /// Add a listing to the pending queue. If duplicate exists, sum quantities.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Models.ApiResponse<PendingListing>>> CreatePendingListing(
        [FromBody] CreatePendingListingDto dto,
        CancellationToken cancellationToken = default)
    {
        // Verify blueprint exists
        var blueprint = await _dbContext.Blueprints
            .FindAsync(new object[] { dto.BlueprintId }, cancellationToken);

        if (blueprint == null)
        {
            return BadRequest(Models.ApiResponse<PendingListing>.ErrorResult("Blueprint not found"));
        }

        // Check for existing duplicate (only against unsynced items)
        var existingItem = await _dbContext.PendingListings
            .FirstOrDefaultAsync(p =>
                !p.IsSynced &&
                p.BlueprintId == dto.BlueprintId &&
                p.Condition == dto.Condition &&
                p.Language == dto.Language &&
                p.SellingPrice == dto.Price &&
                p.IsFoil == dto.IsFoil &&
                p.IsSigned == dto.IsSigned,
                cancellationToken);

        if (existingItem != null)
        {
            // Sum quantities instead of rejecting duplicate
            existingItem.Quantity += dto.Quantity;

            // Update grading data if provided (use latest)
            if (dto.GradingScore.HasValue)
            {
                existingItem.GradingScore = dto.GradingScore;
                existingItem.GradingConditionCode = dto.GradingConditionCode;
                existingItem.GradingCentering = dto.GradingCentering;
                existingItem.GradingCorners = dto.GradingCorners;
                existingItem.GradingEdges = dto.GradingEdges;
                existingItem.GradingSurface = dto.GradingSurface;
                existingItem.GradingConfidence = dto.GradingConfidence;
                existingItem.GradingImagesCount = dto.GradingImagesCount;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(Models.ApiResponse<PendingListing>.SuccessResult(
                existingItem,
                $"Quantity added to existing item. New total: {existingItem.Quantity}"));
        }

        var pendingListing = new PendingListing
        {
            BlueprintId = dto.BlueprintId,
            Quantity = dto.Quantity,
            SellingPrice = dto.Price,
            PurchasePrice = dto.PurchasePrice,
            Condition = dto.Condition,
            Language = dto.Language,
            IsFoil = dto.IsFoil,
            IsSigned = dto.IsSigned,
            Location = dto.Location ?? string.Empty,
            Tag = dto.Tag,
            CreatedAt = DateTime.UtcNow,
            IsSynced = false,
            // Grading data
            GradingScore = dto.GradingScore,
            GradingConditionCode = dto.GradingConditionCode,
            GradingCentering = dto.GradingCentering,
            GradingCorners = dto.GradingCorners,
            GradingEdges = dto.GradingEdges,
            GradingSurface = dto.GradingSurface,
            GradingConfidence = dto.GradingConfidence,
            GradingImagesCount = dto.GradingImagesCount
        };

        _dbContext.PendingListings.Add(pendingListing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetPendingListing),
            new { id = pendingListing.Id },
            Models.ApiResponse<PendingListing>.SuccessResult(pendingListing, "Pending listing created successfully"));
    }

    /// <summary>
    /// Get a single pending listing
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<Models.ApiResponse<PendingListing>>> GetPendingListing(int id, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.PendingListings
            .Include(p => p.Blueprint)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (item == null)
        {
            return NotFound(Models.ApiResponse<PendingListing>.ErrorResult($"Pending listing with ID {id} not found"));
        }

        return Ok(Models.ApiResponse<PendingListing>.SuccessResult(item));
    }

    /// <summary>
    /// Update a pending listing
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<Models.ApiResponse<PendingListing>>> UpdatePendingListing(
        int id,
        [FromBody] CreatePendingListingDto dto,
        CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.PendingListings.FindAsync(new object[] { id }, cancellationToken);

        if (item == null)
        {
            return NotFound(Models.ApiResponse<PendingListing>.ErrorResult($"Pending listing with ID {id} not found"));
        }

        if (item.IsSynced)
        {
            return BadRequest(Models.ApiResponse<PendingListing>.ErrorResult("Cannot update a synced listing"));
        }

        item.BlueprintId = dto.BlueprintId;
        item.Quantity = dto.Quantity;
        item.SellingPrice = dto.Price;
        item.PurchasePrice = dto.PurchasePrice;
        item.Condition = dto.Condition;
        item.Language = dto.Language;
        item.IsFoil = dto.IsFoil;
        item.IsSigned = dto.IsSigned;
        item.Location = dto.Location ?? string.Empty;
        item.Tag = dto.Tag;
        item.SyncError = null; // Clear error on update

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(Models.ApiResponse<PendingListing>.SuccessResult(item, "Pending listing updated successfully"));
    }

    /// <summary>
    /// Delete a pending listing
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<Models.ApiResponse<object>>> DeletePendingListing(int id, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.PendingListings.FindAsync(new object[] { id }, cancellationToken);

        if (item == null)
        {
            return NotFound(Models.ApiResponse<object>.ErrorResult($"Pending listing with ID {id} not found"));
        }

        _dbContext.PendingListings.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(Models.ApiResponse<object>.SuccessResult(null, "Pending listing deleted successfully"));
    }

    /// <summary>
    /// Sync all pending listings to Card Trader
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<Models.ApiResponse<object>>> SyncPendingListings(CancellationToken cancellationToken = default)
    {
        var pendingItems = await _dbContext.PendingListings
            .Include(p => p.Blueprint)
            .Where(p => !p.IsSynced)
            .ToListAsync(cancellationToken);

        int successCount = 0;
        int errorCount = 0;

        foreach (var pending in pendingItems)
        {
            try
            {
                // Create InventoryItem object for the service
                var inventoryItem = new InventoryItem
                {
                    BlueprintId = pending.BlueprintId,
                    Blueprint = pending.Blueprint,
                    Quantity = pending.Quantity,
                    ListingPrice = pending.SellingPrice,
                    Condition = pending.Condition,
                    Language = pending.Language,
                    IsFoil = pending.IsFoil,
                    IsSigned = pending.IsSigned,
                    Location = pending.Location,
                    Tag = pending.Tag,
                    PurchasePrice = pending.PurchasePrice
                };

                // Call Card Trader API
                var cardTraderId = await _cardTraderService.CreateProductOnCardTraderAsync(inventoryItem, cancellationToken);

                // Update PendingListing with sync info
                pending.IsSynced = true;
                pending.SyncedAt = DateTime.UtcNow;
                pending.CardTraderProductId = cardTraderId;
                pending.SyncError = null;

                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing pending listing {Id}", pending.Id);
                pending.SyncError = ex.Message;
                errorCount++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = new
        {
            Total = pendingItems.Count,
            Success = successCount,
            Errors = errorCount
        };

        return Ok(Models.ApiResponse<object>.SuccessResult(
            result,
            $"Sync completed. Success: {successCount}, Errors: {errorCount}"));
    }
}
