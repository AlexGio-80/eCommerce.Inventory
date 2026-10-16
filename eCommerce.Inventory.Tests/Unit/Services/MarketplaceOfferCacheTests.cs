using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// La maschera "Nuovo Prodotto" legge le offerte di una carta per le statistiche, per il prezzo delle
/// regole e a ogni cambio di condizione o lingua; il riprezzo dopo la pubblicazione le rilegge pochi
/// minuti dopo. Il 15/10 erano 1,57 letture a carta solo dalla maschera, ognuna una delle 20 richieste
/// al minuto: la stessa lettura deve servire tutti.
/// </summary>
public class MarketplaceOfferCacheTests
{
    private readonly ManualTimeProvider _time = new();
    private int _fetches;

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private Func<CancellationToken, Task<IEnumerable<CardTraderMarketplaceProductDto>>> Fetch(int offers = 3)
        => async _ =>
        {
            Interlocked.Increment(ref _fetches);
            await Task.Yield();
            return Enumerable.Range(1, offers).Select(i => new CardTraderMarketplaceProductDto { Id = i }).ToList();
        };

    [Fact]
    public async Task Entro_l_eta_massima_la_seconda_richiesta_non_rilegge_il_marketplace()
    {
        var cache = new MarketplaceOfferCache(_time);

        var first = await cache.GetOrFetchAsync(4242, MaxAge, Fetch());
        _time.Advance(TimeSpan.FromMinutes(9));
        var second = await cache.GetOrFetchAsync(4242, MaxAge, Fetch());

        _fetches.Should().Be(1);
        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task Oltre_l_eta_massima_rilegge()
    {
        var cache = new MarketplaceOfferCache(_time);

        await cache.GetOrFetchAsync(4242, MaxAge, Fetch());
        _time.Advance(TimeSpan.FromMinutes(11));
        await cache.GetOrFetchAsync(4242, MaxAge, Fetch());

        _fetches.Should().Be(2);
    }

    [Fact]
    public async Task Statistiche_e_prezzo_delle_regole_chiesti_insieme_condividono_la_stessa_lettura()
    {
        var cache = new MarketplaceOfferCache(_time);

        var both = await Task.WhenAll(
            cache.GetOrFetchAsync(4242, MaxAge, Fetch()),
            cache.GetOrFetchAsync(4242, MaxAge, Fetch()));

        _fetches.Should().Be(1);
        both[0].Should().HaveCount(3);
        both[1].Should().HaveCount(3);
    }

    /// <summary>Il client restituisce una lista vuota anche quando la chiamata fallisce.</summary>
    [Fact]
    public async Task Una_lettura_vuota_non_viene_conservata()
    {
        var cache = new MarketplaceOfferCache(_time);

        (await cache.GetOrFetchAsync(4242, MaxAge, Fetch(offers: 0))).Should().BeEmpty();
        (await cache.GetOrFetchAsync(4242, MaxAge, Fetch())).Should().HaveCount(3);

        _fetches.Should().Be(2);
    }

    [Fact]
    public async Task Una_lettura_fallita_non_blocca_le_successive()
    {
        var cache = new MarketplaceOfferCache(_time);

        var failing = () => cache.GetOrFetchAsync(4242, MaxAge, async _ =>
        {
            await Task.Yield();
            throw new HttpRequestException("Card Trader non risponde");
#pragma warning disable CS0162
            return Enumerable.Empty<CardTraderMarketplaceProductDto>();
#pragma warning restore CS0162
        });

        await failing.Should().ThrowAsync<HttpRequestException>();
        (await cache.GetOrFetchAsync(4242, MaxAge, Fetch())).Should().HaveCount(3);
    }

    [Fact]
    public async Task Carte_diverse_hanno_letture_diverse()
    {
        var cache = new MarketplaceOfferCache(_time);

        await cache.GetOrFetchAsync(4242, MaxAge, Fetch());
        await cache.GetOrFetchAsync(4343, MaxAge, Fetch());

        _fetches.Should().Be(2);
    }
}
