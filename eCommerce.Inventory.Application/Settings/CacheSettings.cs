namespace eCommerce.Inventory.Application.Settings;

/// <summary>
/// Settings for Redis caching of Card Trader static data
/// </summary>
public class CacheSettings
{
    public const string SectionName = "CacheSettings";

    /// <summary>
    /// Time-to-live for Games cache in hours (default: 24)
    /// </summary>
    public int GamesTtlHours { get; set; } = 24;

    /// <summary>
    /// Time-to-live for Expansions cache in hours (default: 12)
    /// </summary>
    public int ExpansionsTtlHours { get; set; } = 12;

    /// <summary>
    /// Time-to-live for Blueprints cache in hours (default: 6)
    /// </summary>
    public int BlueprintsTtlHours { get; set; } = 6;
}