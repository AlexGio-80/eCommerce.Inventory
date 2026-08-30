using System.Collections.Concurrent;
using System.Threading.Channels;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Coda in memoria dei blueprint da riprezzare.
///
/// In memoria è sufficiente: una richiesta persa per un riavvio viene comunque recuperata
/// dall'esecuzione notturna, e la carta interessata è appena stata venduta o appena messa
/// in vendita, quindi rientra fra quelle di valore che vengono valutate ogni notte. Rendere
/// durevole questa coda aggiungerebbe complessità senza risolvere un problema reale.
/// </summary>
public class PriceRefreshQueue : IPriceRefreshQueue
{
    private readonly Channel<PriceRefreshRequest> _channel;
    private readonly ConcurrentDictionary<int, byte> _pending = new();
    private readonly ILogger<PriceRefreshQueue> _logger;

    public PriceRefreshQueue(ILogger<PriceRefreshQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateUnbounded<PriceRefreshRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public int PendingCount => _pending.Count;

    public void Enqueue(int blueprintId, string reason, PricingTrigger trigger)
    {
        // Un ordine con più copie della stessa carta, o più inserzioni dello stesso blueprint
        // pubblicate insieme, genererebbero richieste identiche: una sola valutazione basta,
        // e le chiamate API sono la risorsa scarsa.
        if (!_pending.TryAdd(blueprintId, 0))
        {
            _logger.LogDebug("Blueprint {BlueprintId} già in coda per rivalutazione, richiesta ignorata", blueprintId);
            return;
        }

        if (!_channel.Writer.TryWrite(new PriceRefreshRequest(blueprintId, reason, trigger)))
        {
            _pending.TryRemove(blueprintId, out _);
            _logger.LogWarning("Impossibile accodare il blueprint {BlueprintId} per rivalutazione", blueprintId);
            return;
        }

        _logger.LogInformation(
            "Blueprint {BlueprintId} accodato per rivalutazione immediata ({Reason}). In attesa: {Pending}",
            blueprintId, reason, _pending.Count);
    }

    public async Task<PriceRefreshRequest> DequeueAsync(CancellationToken cancellationToken)
    {
        var request = await _channel.Reader.ReadAsync(cancellationToken);
        _pending.TryRemove(request.BlueprintId, out _);
        return request;
    }
}
