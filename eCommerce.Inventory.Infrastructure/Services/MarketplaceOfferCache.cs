using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Implementa <see cref="IMarketplaceOfferCache"/> in memoria. <b>Singleton</b>: la maschera e il
/// riprezzo dopo la pubblicazione girano in scope diversi e devono vedere le stesse letture.
///
/// In memoria basta: perdere la cache a un riavvio costa al più una lettura in più per carta.
/// Le voci più vecchie di <see cref="Retention"/> si scartano a ogni lettura nuova, così la
/// memoria resta proporzionale alle carte toccate negli ultimi minuti.
/// </summary>
public class MarketplaceOfferCache : IMarketplaceOfferCache
{
    /// <summary>
    /// Età massima delle offerte riusate nel giro della maschera: scelta della carta, statistiche,
    /// prezzo proposto e riprezzo dopo la pubblicazione. Un blocco di 20 carte si lista e si invia in
    /// pochi minuti, e in quel tempo il mercato di una carta non si muove in modo che conti.
    /// </summary>
    public static readonly TimeSpan ListingWorkflowMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>Oltre questa età una voce non serve più a nessun chiamante e si può scartare.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<int, Entry> _entries = new();

    public MarketplaceOfferCache() : this(TimeProvider.System) { }

    public MarketplaceOfferCache(TimeProvider time)
    {
        _time = time;
    }

    public async Task<IReadOnlyList<CardTraderMarketplaceProductDto>> GetOrFetchAsync(
        int cardTraderBlueprintId,
        TimeSpan maxAge,
        Func<CancellationToken, Task<IEnumerable<CardTraderMarketplaceProductDto>>> fetch,
        CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            if (!_entries.TryGetValue(cardTraderBlueprintId, out entry!)
                || (entry.FetchedAt is { } fetchedAt && now - fetchedAt > maxAge))
            {
                Evict(now);
                entry = new Entry();
                entry.Task = FetchAsync(cardTraderBlueprintId, entry, fetch, cancellationToken);
                _entries[cardTraderBlueprintId] = entry;
            }
        }

        return await entry.Task;
    }

    private async Task<IReadOnlyList<CardTraderMarketplaceProductDto>> FetchAsync(
        int cardTraderBlueprintId,
        Entry entry,
        Func<CancellationToken, Task<IEnumerable<CardTraderMarketplaceProductDto>>> fetch,
        CancellationToken cancellationToken)
    {
        // Lascia al chiamante il tempo di registrare la voce prima che la lettura si concluda.
        await Task.Yield();

        List<CardTraderMarketplaceProductDto> offers;
        try
        {
            offers = (await fetch(cancellationToken)).ToList();
        }
        catch
        {
            Forget(cardTraderBlueprintId, entry);
            throw;
        }

        lock (_lock)
        {
            if (offers.Count == 0)
            {
                // Il client restituisce una lista vuota anche quando la chiamata fallisce:
                // conservarla nasconderebbe il mercato vero per tutta la durata della voce.
                Forget(cardTraderBlueprintId, entry);
            }
            else
            {
                entry.FetchedAt = _time.GetUtcNow();
            }
        }

        return offers;
    }

    private void Forget(int cardTraderBlueprintId, Entry entry)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(cardTraderBlueprintId, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(cardTraderBlueprintId);
            }
        }
    }

    private void Evict(DateTimeOffset now)
    {
        var expired = _entries
            .Where(e => e.Value.FetchedAt is { } fetchedAt && now - fetchedAt > Retention)
            .Select(e => e.Key)
            .ToList();

        foreach (var key in expired)
        {
            _entries.Remove(key);
        }
    }

    private sealed class Entry
    {
        public Task<IReadOnlyList<CardTraderMarketplaceProductDto>> Task { get; set; } = null!;

        /// <summary>Null finché la lettura è in corso: chi arriva nel frattempo la aspetta.</summary>
        public DateTimeOffset? FetchedAt { get; set; }
    }
}
