using Microsoft.Extensions.Diagnostics.HealthChecks;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Settings;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace eCommerce.Inventory.Api.HealthChecks;

/// <summary>
/// Health check for Card Trader API connectivity
/// Uses cached Games endpoint as lightweight probe
/// </summary>
public class CardTraderApiHealthCheck : IHealthCheck
{
    private readonly ICardTraderApiService _cardTraderApi;
    private readonly ICacheService _cache;
    private readonly CacheSettings _cacheSettings;
    private readonly ILogger<CardTraderApiHealthCheck> _logger;

    public CardTraderApiHealthCheck(
        ICardTraderApiService cardTraderApi,
        ICacheService cache,
        IOptions<CacheSettings> cacheSettings,
        ILogger<CardTraderApiHealthCheck> logger)
    {
        _cardTraderApi = cardTraderApi;
        _cache = cache;
        _cacheSettings = cacheSettings.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Try to get games from cache first (lightweight)
            var cachedGames = await _cache.GetAsync<List<eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs.CardTraderGameDto>>("cardtrader:games", cancellationToken);

            if (cachedGames != null && cachedGames.Count > 0)
            {
                stopwatch.Stop();
                _logger.LogDebug("Card Trader API health check passed (cache hit) in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

                return HealthCheckResult.Healthy(
                    "Card Trader API is healthy (cached data available)",
                    new Dictionary<string, object>
                    {
                        ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                        ["source"] = "cache",
                        ["gamesCount"] = cachedGames.Count
                    });
            }

            // If cache miss, make actual API call (this is heavier but validates full connectivity)
            var games = await _cardTraderApi.SyncGamesAsync(cancellationToken);

            stopwatch.Stop();

            var gamesList = games?.ToList();
            if (gamesList != null && gamesList.Count > 0)
            {
                _logger.LogDebug("Card Trader API health check passed (API call) in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

                return HealthCheckResult.Healthy(
                    "Card Trader API is healthy",
                    new Dictionary<string, object>
                    {
                        ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                        ["source"] = "api",
                        ["gamesCount"] = gamesList.Count
                    });
            }

            stopwatch.Stop();
            _logger.LogWarning("Card Trader API health check returned empty games list in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Degraded(
                "Card Trader API returned empty games list",
                null,
                new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["gamesCount"] = 0
                });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Card Trader API health check failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Unhealthy(
                "Card Trader API connection failed",
                ex,
                new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["error"] = ex.Message
                });
        }
    }
}