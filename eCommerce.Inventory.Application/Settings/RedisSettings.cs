namespace eCommerce.Inventory.Application.Settings;

/// <summary>
/// Redis connection settings
/// </summary>
public class RedisSettings
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis connection string (e.g., "localhost:6379" or "myredis.redis.cache.windows.net:6380,password=...,ssl=True")
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Whether Redis caching is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Instance name prefix for keys (e.g., "ecommerce-inventory")
    /// </summary>
    public string InstanceName { get; set; } = "ecommerce-inventory";
}