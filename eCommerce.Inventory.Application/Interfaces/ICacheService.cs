using System.Collections.Generic;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Interface for distributed cache service (Redis)
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get a value from cache
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Set a value in cache with expiration
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Remove a value from cache
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove multiple values by pattern (uses SCAN)
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStats
{
    public long TotalKeys { get; set; }
    public long HitCount { get; set; }
    public long MissCount { get; set; }
    public double HitRatio => TotalRequests > 0 ? (double)HitCount / TotalRequests : 0;
    public long TotalRequests => HitCount + MissCount;
}