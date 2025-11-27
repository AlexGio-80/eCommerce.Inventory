using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Policies;

/// <summary>
/// Polly resilience policies for Card Trader API HTTP calls
/// </summary>
public static class CardTraderPolicies
{
    /// <summary>
    /// Retry policy with exponential backoff
    /// Retries 3 times: after 2s, 4s, 8s
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx and 408
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.GetLogger();
                    if (logger != null)
                    {
                        logger.LogWarning(
                            "Card Trader API call failed. Retry {RetryCount} after {Delay}ms. Status: {StatusCode}, Reason: {Reason}",
                            retryCount,
                            timespan.TotalMilliseconds,
                            outcome.Result?.StatusCode,
                            outcome.Result?.ReasonPhrase ?? outcome.Exception?.Message);
                    }
                });
    }

    /// <summary>
    /// Circuit breaker policy
    /// Opens circuit after 5 consecutive failures, stays open for 30 seconds
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, duration) =>
                {
                    // Log circuit breaker opened
                    Console.WriteLine($"Circuit breaker opened for {duration.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.ReasonPhrase}");
                },
                onReset: () =>
                {
                    // Log circuit breaker reset
                    Console.WriteLine("Circuit breaker reset - calls will resume");
                },
                onHalfOpen: () =>
                {
                    // Log circuit breaker half-open (testing if service recovered)
                    Console.WriteLine("Circuit breaker half-open - testing if service recovered");
                });
    }

    /// <summary>
    /// Timeout policy - 30 seconds per request
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy()
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Extension method to get logger from Polly context
    /// </summary>
    private static ILogger? GetLogger(this Polly.Context context)
    {
        if (context.TryGetValue("logger", out var logger))
        {
            return logger as ILogger;
        }
        return null;
    }
}
