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
    /// Returns all existing listings for a given blueprint:
    /// - InventoryItems (synced from CT, represent the actual current state)
    /// - Plus any unsynced PendingListings not yet pushed to CT
    /// Used to populate the "Le mie inserzioni" panel in the Create Listing form.
    /// </summary>
    [HttpGet("by-blueprint/{blueprintId:int}")]
    public async Task<ActionResult<Models.ApiResponse<IEnumerable<BlueprintListingInfoDto>>>> GetListingsByBlueprint(
        int blueprintId,
        CancellationToken cancellationToken = default)
    {
        // InventoryItems = what's currently on CT (after last product sync)
        var inventoryItems = await _dbContext.InventoryItems
            .AsNoTracking()
            .Where(ii => ii.BlueprintId == blueprintId)
            .ToListAsync(cancellationToken);

        // All PendingListings for this blueprint (synced or not)
        var pendingListings = await _dbContext.PendingListings
            .AsNoTracking()
            .Where(pl => pl.BlueprintId == blueprintId)
            .ToListAsync(cancellationToken);

        var result = new List<BlueprintListingInfoDto>();

        // Map InventoryItems → enrich with matching unsynced PendingListing if present
        var unsyncedByProductId = pendingListings
            .Where(pl => !pl.IsSynced && pl.CardTraderProductId.HasValue)
            .ToDictionary(pl => pl.CardTraderProductId!.Value, pl => pl);

        foreach (var ii in inventoryItems)
        {
            PendingListing? pendingEdit = ii.CardTraderProductId.HasValue
                ? unsyncedByProductId.GetValueOrDefault(ii.CardTraderProductId.Value)
                : null;

            // If there is a pending edit queued, show its values (latest intent)
            var source = pendingEdit != null
                ? new
                {
                    Quantity = pendingEdit.Quantity,
                    SellingPrice = pendingEdit.SellingPrice,
                    PurchasePrice = pendingEdit.PurchasePrice,
                    Condition = pendingEdit.Condition,
                    Language = pendingEdit.Language,
                    IsFoil = pendingEdit.IsFoil,
                    IsSigned = pendingEdit.IsSigned,
                    Location = pendingEdit.Location,
                    Tag = pendingEdit.Tag
                }
                : new
                {
                    Quantity = ii.Quantity,
                    SellingPrice = ii.ListingPrice,
                    PurchasePrice = ii.PurchasePrice,
                    Condition = ii.Condition,
                    Language = ii.Language,
                    IsFoil = ii.IsFoil,
                    IsSigned = ii.IsSigned,
                    Location = ii.Location,
                    Tag = ii.Tag
                };

            result.Add(new BlueprintListingInfoDto
            {
                InventoryItemId = ii.Id,
                PendingListingId = pendingEdit?.Id,
                CardTraderProductId = ii.CardTraderProductId,
                Quantity = source.Quantity,
                SellingPrice = source.SellingPrice,
                PurchasePrice = source.PurchasePrice,
                Condition = source.Condition,
                Language = source.Language,
                IsFoil = source.IsFoil,
                IsSigned = source.IsSigned,
                Location = source.Location,
                Tag = source.Tag,
                // pending-edit = an update is queued but not yet sent; synced = on CT, no pending changes; ct-native = on CT, never managed by us
                Status = pendingEdit != null ? "pending-edit"
                    : pendingListings.Any(pl => pl.CardTraderProductId == ii.CardTraderProductId && pl.IsSynced) ? "synced"
                    : "ct-native"
            });
        }

        // Add unsynced PendingListings with no CardTraderProductId (pure new listings not yet on CT)
        var pureNew = pendingListings
            .Where(pl => !pl.IsSynced && !pl.CardTraderProductId.HasValue)
            .ToList();

        foreach (var pl in pureNew)
        {
            result.Add(new BlueprintListingInfoDto
            {
                InventoryItemId = null,
                PendingListingId = pl.Id,
                CardTraderProductId = null,
                Quantity = pl.Quantity,
                SellingPrice = pl.SellingPrice,
                PurchasePrice = pl.PurchasePrice,
                Condition = pl.Condition,
                Language = pl.Language,
                IsFoil = pl.IsFoil,
                IsSigned = pl.IsSigned,
                Location = pl.Location,
                Tag = pl.Tag,
                Status = "pending-new"
            });
        }

        return Ok(Models.ApiResponse<IEnumerable<BlueprintListingInfoDto>>.SuccessResult(result));
    }

    /// <summary>
    /// Add a listing to the pending queue.
    /// - Normal mode: if duplicate exists (same blueprint/condition/language/price/foil/signed), sums quantities.
    /// - Update mode (IsUpdate=true): creates a new record with CardTraderProductId pre-filled so sync calls UPDATE on CT.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Models.ApiResponse<PendingListing>>> CreatePendingListing(
        [FromBody] CreatePendingListingDto dto,
        CancellationToken cancellationToken = default)
    {
        var blueprint = await _dbContext.Blueprints
            .FindAsync(new object[] { dto.BlueprintId }, cancellationToken);

        if (blueprint == null)
        {
            return BadRequest(Models.ApiResponse<PendingListing>.ErrorResult("Blueprint not found"));
        }

        // Update-mode: skip duplicate check, always create a fresh record linked to the CT product
        if (!dto.IsUpdate)
        {
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
                existingItem.Quantity += dto.Quantity;

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
            IsUpdate = dto.IsUpdate,
            CardTraderProductId = dto.CardTraderProductId,
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
    /// Update a pending listing.
    /// If the listing is already synced (IsSynced=true), it is re-queued as an UPDATE operation:
    /// IsSynced is reset to false and IsUpdate is set to true so the next sync calls CT's update API.
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
        item.SyncError = null;

        // If re-editing a synced listing: re-queue it as an UPDATE (not a new CREATE on CT)
        if (item.IsSynced)
        {
            item.IsSynced = false;
            item.IsUpdate = true;
            item.SyncedAt = null;
        }

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
    /// Sync all pending listings to Card Trader.
    /// Records with IsUpdate=true and CardTraderProductId set call CT's update API instead of create.
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
                    PurchasePrice = pending.PurchasePrice,
                    CardTraderProductId = pending.CardTraderProductId
                };

                if (pending.IsUpdate && pending.CardTraderProductId.HasValue)
                {
                    await _cardTraderService.UpdateProductOnCardTraderAsync(inventoryItem, cancellationToken);
                    pending.IsSynced = true;
                    pending.SyncedAt = DateTime.UtcNow;
                    pending.SyncError = null;
                }
                else
                {
                    var cardTraderId = await _cardTraderService.CreateProductOnCardTraderAsync(inventoryItem, cancellationToken);
                    pending.IsSynced = true;
                    pending.SyncedAt = DateTime.UtcNow;
                    pending.CardTraderProductId = cardTraderId;
                    pending.SyncError = null;
                }

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

public class BlueprintListingInfoDto
{
    public int? InventoryItemId { get; set; }
    public int? PendingListingId { get; set; }
    public int? CardTraderProductId { get; set; }
    public int Quantity { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Tag { get; set; }
    /// <summary>
    /// synced = on CT, no pending changes
    /// pending-edit = on CT, an update is queued
    /// ct-native = on CT, never managed via our software
    /// pending-new = in queue, not yet on CT
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
