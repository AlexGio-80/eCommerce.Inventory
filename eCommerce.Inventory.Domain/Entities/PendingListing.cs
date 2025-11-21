namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Staging table for product listings before syncing to Card Trader.
/// Allows review, edit, and batch sync with error tracking.
/// </summary>
public class PendingListing
{
    public int Id { get; set; }

    // Blueprint reference
    public int BlueprintId { get; set; }
    public virtual Blueprint Blueprint { get; set; } = null!;

    // Listing data (same as InventoryItem)
    public int Quantity { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Tag { get; set; }

    // Sync state tracking
    public bool IsSynced { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAt { get; set; }
    public string? SyncError { get; set; }

    // References after successful sync
    public int? CardTraderProductId { get; set; }
    public int? InventoryItemId { get; set; } // Link to created InventoryItem
    public virtual InventoryItem? InventoryItem { get; set; }
}
