using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Il limite di 20 richieste al minuto è condiviso da tutta l'applicazione. Il 15/10, dopo ogni
/// blocco di carte pubblicate, il riprezzo in background si prendeva tutta la finestra e la maschera
/// "Nuovo Prodotto" aspettava fino a 55 secondi per carta: le richieste interattive devono passare
/// davanti, e trovare qualche permesso libero mentre la maschera è in uso.
/// </summary>
public class CardTraderRateLimiterTests
{
    private readonly ManualTimeProvider _time = new();

    private CardTraderRateLimiter Limiter() => new(NullLogger<CardTraderRateLimiter>.Instance, _time);

    private static Task Background(CardTraderRateLimiter limiter, CancellationToken cancellationToken = default)
        => limiter.AcquireAsync(cancellationToken).AsTask();

    private static Task Interactive(CardTraderRateLimiter limiter)
    {
        using var _ = CardTraderRequestPriority.BeginInteractive();
        return limiter.AcquireAsync().AsTask();
    }

    [Fact]
    public void Venti_richieste_passano_subito_la_ventunesima_aspetta_la_finestra_successiva()
    {
        using var limiter = Limiter();

        var first = Enumerable.Range(0, 20).Select(_ => Background(limiter)).ToList();
        var next = Background(limiter);

        first.Should().OnlyContain(t => t.IsCompletedSuccessfully);
        next.IsCompleted.Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(59));
        next.IsCompleted.Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        next.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void Con_la_maschera_in_uso_il_background_lascia_libere_cinque_richieste_per_finestra()
    {
        using var limiter = Limiter();

        Interactive(limiter).IsCompletedSuccessfully.Should().BeTrue();

        // Il riprezzo dopo la pubblicazione chiede tutto quello che può: arriva a 15 richieste in
        // tutto nella finestra, compresa quella della maschera.
        var background = Enumerable.Range(0, 20).Select(_ => Background(limiter)).ToList();
        background.Count(t => t.IsCompletedSuccessfully).Should().Be(14);

        // La maschera non aspetta: le ultime cinque sono sue.
        var mask = Enumerable.Range(0, 5).Select(_ => Interactive(limiter)).ToList();
        mask.Should().OnlyContain(t => t.IsCompletedSuccessfully);

        // Il limite complessivo resta 20.
        Interactive(limiter).IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Quando_nessuno_usa_la_maschera_il_background_ha_di_nuovo_tutto_il_limite()
    {
        using var limiter = Limiter();

        Interactive(limiter).IsCompletedSuccessfully.Should().BeTrue();
        _time.Advance(TimeSpan.FromMinutes(6));

        var background = Enumerable.Range(0, 20).Select(_ => Background(limiter)).ToList();
        background.Should().OnlyContain(t => t.IsCompletedSuccessfully);
    }

    [Fact]
    public void Alla_nuova_finestra_la_richiesta_interattiva_passa_davanti_al_background_in_coda()
    {
        using var limiter = Limiter();

        Enumerable.Range(0, 20).Select(_ => Background(limiter)).ToList();
        var queuedBackground = Enumerable.Range(0, 25).Select(_ => Background(limiter)).ToList();
        var mask = Interactive(limiter);

        mask.IsCompleted.Should().BeFalse();

        _time.Advance(TimeSpan.FromMinutes(1));

        mask.IsCompletedSuccessfully.Should().BeTrue();
        // 20 meno la riserva della maschera, meno la sua richiesta già servita.
        queuedBackground.Count(t => t.IsCompletedSuccessfully).Should().Be(14);

        // Le altre restano in coda, in ordine d'arrivo, per le finestre successive.
        _time.Advance(TimeSpan.FromMinutes(1));
        queuedBackground.Count(t => t.IsCompletedSuccessfully).Should().Be(25);
    }

    [Fact]
    public void Una_richiesta_annullata_in_coda_non_consuma_un_permesso()
    {
        using var limiter = Limiter();
        using var cts = new CancellationTokenSource();

        Enumerable.Range(0, 20).Select(_ => Background(limiter)).ToList();
        var cancelled = Background(limiter, cts.Token);
        var waiting = Enumerable.Range(0, 20).Select(_ => Background(limiter)).ToList();

        cts.Cancel();
        cancelled.IsCanceled.Should().BeTrue();

        _time.Advance(TimeSpan.FromMinutes(1));
        waiting.Should().OnlyContain(t => t.IsCompletedSuccessfully);
    }
}
