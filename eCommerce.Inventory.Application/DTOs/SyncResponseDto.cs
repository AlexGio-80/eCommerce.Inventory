namespace eCommerce.Inventory.Application.DTOs;

/// <summary>
/// DTO for sync operation response
/// Contains statistics about the synchronization results
/// </summary>
public class SyncResponseDto
{
    /// <summary>
    /// Total number of records added to database
    /// </summary>
    public int Added { get; set; } = 0;

    /// <summary>
    /// Total number of records updated in database
    /// </summary>
    public int Updated { get; set; } = 0;

    /// <summary>
    /// Total number of records that failed to sync
    /// </summary>
    public int Failed { get; set; } = 0;

    /// <summary>
    /// Detailed sync results by entity type
    /// </summary>
    public SyncEntityResultDto Games { get; set; } = new();
    public SyncEntityResultDto Categories { get; set; } = new();
    public SyncEntityResultDto Expansions { get; set; } = new();
    public SyncEntityResultDto Blueprints { get; set; } = new();
    public SyncEntityResultDto Properties { get; set; } = new();

    /// <summary>
    /// Timestamp when sync started
    /// </summary>
    public DateTime SyncStartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when sync completed
    /// </summary>
    public DateTime SyncEndTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total duration of sync operation
    /// </summary>
    public TimeSpan Duration => SyncEndTime - SyncStartTime;

    /// <summary>
    /// Error message if sync failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Detailed result for a single entity type sync
/// </summary>
public class SyncEntityResultDto
{
    /// <summary>
    /// Whether this entity type was synced
    /// </summary>
    public bool WasRequested { get; set; } = false;

    /// <summary>
    /// Number of records added for this entity type
    /// </summary>
    public int Added { get; set; } = 0;

    /// <summary>
    /// Number of records updated for this entity type
    /// </summary>
    public int Updated { get; set; } = 0;

    /// <summary>
    /// Number of records that failed for this entity type
    /// </summary>
    public int Failed { get; set; } = 0;

    /// <summary>
    /// Total records processed for this entity type
    /// </summary>
    public int Total => Added + Updated;

    /// <summary>
    /// Error message specific to this entity type (if any)
    /// </summary>
    public string? ErrorMessage { get; set; }
}
