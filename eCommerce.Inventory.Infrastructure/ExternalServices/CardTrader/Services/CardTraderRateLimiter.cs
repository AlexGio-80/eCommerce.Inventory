using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

public class CardTraderRateLimiter : IDisposable
{
    private readonly RateLimiter _limiter;
    private readonly ILogger<CardTraderRateLimiter> _logger;

    public CardTraderRateLimiter(ILogger<CardTraderRateLimiter> logger)
    {
        _logger = logger;

        // Card Trader limits: 20 requests per minute (conservative estimate)
        // Adjust based on actual API documentation
        _limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    }

    public async ValueTask AcquireAsync(CancellationToken cancellationToken = default)
    {
        var lease = await _limiter.AcquireAsync(1, cancellationToken);

        if (lease.IsAcquired)
        {
            return;
        }

        _logger.LogWarning("Card Trader rate limit exceeded. Waiting for permit...");

        // If not acquired immediately, we might want to wait or throw.
        // The AcquireAsync above waits if there's queue space. 
        // If we are here, it means we failed to acquire even after queueing or queue is full?
        // Actually AcquireAsync waits if permit is not available but queue is available.
        // It returns IsAcquired=false if queue is full or other failure.

        throw new InvalidOperationException("Card Trader rate limit exceeded and queue is full.");
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
