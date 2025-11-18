namespace eCommerce.Inventory.Application.DTOs;

/// <summary>
/// DTO for requesting selective synchronization of Card Trader data
/// Allows clients to choose which entities to sync
/// </summary>
public class SyncRequestDto
{
    /// <summary>
    /// Sync Games from Card Trader
    /// </summary>
    public bool SyncGames { get; set; } = false;

    /// <summary>
    /// Sync Categories from Card Trader (mapped to Game metadata)
    /// </summary>
    public bool SyncCategories { get; set; } = false;

    /// <summary>
    /// Sync Expansions from Card Trader
    /// </summary>
    public bool SyncExpansions { get; set; } = false;

    /// <summary>
    /// Sync Blueprints from Card Trader
    /// </summary>
    public bool SyncBlueprints { get; set; } = false;

    /// <summary>
    /// Sync Properties (Blueprint metadata) from Card Trader
    /// </summary>
    public bool SyncProperties { get; set; } = false;

    /// <summary>
    /// Determine if any sync is requested
    /// </summary>
    public bool IsEmpty => !SyncGames && !SyncCategories && !SyncExpansions && !SyncBlueprints && !SyncProperties;
}
