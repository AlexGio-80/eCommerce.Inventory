using System.Text.Json;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Redis implementation of ICacheService
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly ILogger<RedisCacheService> _logger;
    private readonly RedisSettings _redisSettings;
    private readonly ConnectionMultiplexer? _connectionMultiplexer;
    private readonly IDatabase? _database;
    private readonly string _keyPrefix;
    private long _hitCount = 0;
    private long _missCount = 0;

    public RedisCacheService(
        ILogger<RedisCacheService> logger,
        IOptions<RedisSettings> redisSettings)
    {
        _logger = logger;
        _redisSettings = redisSettings.Value;
        _keyPrefix = $"{_redisSettings.InstanceName}:";

        if (_redisSettings.Enabled && !string.IsNullOrWhiteSpace(_redisSettings.ConnectionString))
        {
            try
            {
                var configuration = ConfigurationOptions.Parse(_redisSettings.ConnectionString);
                configuration.AbortOnConnectFail = false;
                configuration.ConnectRetry = 3;
                configuration.ConnectTimeout = 5000;
                configuration.SyncTimeout = 5000;

                _connectionMultiplexer = ConnectionMultiplexer.Connect(configuration);
                _database = _connectionMultiplexer.GetDatabase();
                _logger.LogInformation("Redis cache connected to {ConnectionString}", _redisSettings.ConnectionString);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Redis. Caching will be disabled. ConnectionString: {ConnectionString}", _redisSettings.ConnectionString);
                _connectionMultiplexer = null;
                _database = null;
            }
        }
        else
        {
            _logger.LogInformation("Redis caching is disabled in configuration");
        }
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        if (_database == null)
        {
            Interlocked.Increment(ref _missCount);
            return null;
        }

        try
        {
            var fullKey = _keyPrefix + key;
            var value = await _database.StringGetAsync(fullKey);

            if (!value.HasValue)
            {
                Interlocked.Increment(ref _missCount);
                return null;
            }

            Interlocked.Increment(ref _hitCount);
            var result = JsonSerializer.Deserialize<T>(value!);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting cache key {Key}", key);
            Interlocked.Increment(ref _missCount);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        if (_database == null || value == null)
            return;

        try
        {
            var fullKey = _keyPrefix + key;
            var json = JsonSerializer.Serialize(value);
            await _database.StringSetAsync(fullKey, json, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting cache key {Key}", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_database == null)
            return;

        try
        {
            var fullKey = _keyPrefix + key;
            await _database.KeyDeleteAsync(fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing cache key {Key}", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_connectionMultiplexer == null || _database == null)
            return;

        try
        {
            var fullPattern = _keyPrefix + pattern;
            var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints()[0]);
            var keys = server.Keys(pattern: fullPattern).ToArray();

            if (keys.Length > 0)
            {
                await _database.KeyDeleteAsync(keys);
                _logger.LogInformation("Removed {Count} cache keys matching pattern {Pattern}", keys.Length, pattern);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing cache keys by pattern {Pattern}", pattern);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_database == null)
            return false;

        try
        {
            var fullKey = _keyPrefix + key;
            return await _database.KeyExistsAsync(fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking cache key existence {Key}", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new CacheStats
        {
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount)
        };

        if (_connectionMultiplexer != null && _database != null)
        {
            try
            {
                var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints()[0]);
                var info = await server.InfoAsync("keyspace");
                // Parse keyspace info for total keys (simplified)
                var infoStr = info.ToString();
                var lines = infoStr.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("db0:keys="))
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2 && long.TryParse(parts[1].Split(',')[0], out var keys))
                        {
                            stats.TotalKeys = keys;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Ignore stats errors
            }
        }

        return stats;
    }
}