using Microsoft.Extensions.Diagnostics.HealthChecks;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Settings;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace eCommerce.Inventory.Api.HealthChecks;

/// <summary>
/// Health check for Redis connectivity
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly ICacheService _cache;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(
        ICacheService cache,
        IOptions<RedisSettings> redisSettings,
        ILogger<RedisHealthCheck> logger)
    {
        _cache = cache;
        _redisSettings = redisSettings.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // If Redis is disabled in config, return healthy (not required)
        if (!_redisSettings.Enabled)
        {
            _logger.LogDebug("Redis health check skipped - caching is disabled in configuration");
            return HealthCheckResult.Healthy(
                "Redis caching is disabled in configuration",
                new Dictionary<string, object>
                {
                    ["enabled"] = false
                });
        }

        var stopwatch = Stopwatch.StartNew();

        // Add a timeout for the health check itself (fail fast if Redis is slow)
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            // Test cache with a simple key
            var testKey = $"healthcheck:{Guid.NewGuid()}";
            var testValue = "test";

            await _cache.SetAsync(testKey, testValue, TimeSpan.FromSeconds(10), timeoutCts.Token);
            var retrieved = await _cache.GetAsync<string>(testKey, timeoutCts.Token);
            await _cache.RemoveAsync(testKey, timeoutCts.Token);

            stopwatch.Stop();

            if (retrieved == testValue)
            {
                // Also get cache stats for additional info
                var stats = await _cache.GetStatsAsync(cancellationToken);

                _logger.LogDebug("Redis health check passed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

                return HealthCheckResult.Healthy(
                    "Redis connection is healthy",
                    new Dictionary<string, object>
                    {
                        ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                        ["enabled"] = true,
                        ["hitCount"] = stats.HitCount,
                        ["missCount"] = stats.MissCount,
                        ["totalKeys"] = stats.TotalKeys
                    });
            }

            stopwatch.Stop();
            _logger.LogWarning("Redis health check failed - value mismatch in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Degraded(
                "Redis connection test failed - value mismatch",
                null,
                new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["enabled"] = true
                });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Redis health check failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Unhealthy(
                "Redis connection failed",
                ex,
                new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["enabled"] = true,
                    ["error"] = ex.Message
                });
        }
    }
}