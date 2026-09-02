using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Pricing;

/// <summary>
/// Il coordinatore esiste per una ragione sola: tenere una esecuzione dell'autopricer per
/// volta. Il limite verso Card Trader è di 20 richieste al minuto ed è condiviso, quindi due
/// esecuzioni in parallelo non vanno il doppio più veloci — si dimezzano a vicenda e
/// rallentano anche le sincronizzazioni.
///
/// La parte fragile non è il rifiuto della seconda richiesta, ma la liberazione dello slot:
/// se restasse occupato dopo un errore, nessuna esecuzione ripartirebbe più fino al riavvio
/// del servizio — compresa la notturna, che passa di qui anch'essa.
/// </summary>
public class PricingRunCoordinatorTests
{
    /// <summary>
    /// Coordinatore con l'esecuzione vera sostituita da un interruttore, per governare
    /// quando finisce senza tirare dentro magazzino, database e API di Card Trader.
    /// </summary>
    private sealed class CoordinatoreGovernabile : PricingRunCoordinator
    {
        private readonly TaskCompletionSource _sbloccaEsecuzione = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CoordinatoreGovernabile()
            : base(Mock.Of<IServiceProvider>(), Mock.Of<IHostApplicationLifetime>(),
                   NullLogger<PricingRunCoordinator>.Instance)
        {
        }

        /// <summary>Se valorizzata, l'esecuzione fallisce con questa eccezione.</summary>
        public Exception? Fallimento { get; set; }

        /// <summary>Segnalato quando l'esecuzione è effettivamente partita.</summary>
        public TaskCompletionSource Partita { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken TokenRicevuto { get; private set; }

        public void Concludi() => _sbloccaEsecuzione.TrySetResult();

        protected override async Task RunCoreAsync(IPricingRunProgress active, CancellationToken token)
        {
            TokenRicevuto = token;
            active.Phase = "In corso";
            Partita.TrySetResult();

            // Volutamente non reagisce all'annullamento: fermarsi è compito del motore, che
            // controlla il token fra un blueprint e il successivo. Qui interessa solo che il
            // coordinatore quel token lo annulli davvero, e che lo dica a chi guarda.
            await _sbloccaEsecuzione.Task;

            if (Fallimento != null) throw Fallimento;
        }
    }

    private static PricingRunStartRequest Richiesta(string descrizione = "Esecuzione manuale")
        => new(PricingTrigger.Manual, descrizione);

    [Fact]
    public async Task La_seconda_esecuzione_viene_rifiutata_finche_la_prima_e_in_corso()
    {
        var coordinatore = new CoordinatoreGovernabile();

        var prima = coordinatore.Start(Richiesta("Esecuzione notturna"));
        await coordinatore.Partita.Task;

        var seconda = coordinatore.Start(Richiesta("Esecuzione manuale"));

        prima.Started.Should().BeTrue();
        seconda.Started.Should().BeFalse();

        // Chi viene rifiutato deve poter dire *quale* esecuzione occupa il posto: un "occupato"
        // senza nome non permette di decidere se attendere o interrompere.
        seconda.Status.Description.Should().Be("Esecuzione notturna");

        coordinatore.Concludi();
        await prima.Completion;
    }

    [Fact]
    public async Task Lo_slot_si_libera_a_esecuzione_conclusa()
    {
        var coordinatore = new CoordinatoreGovernabile();

        var prima = coordinatore.Start(Richiesta());
        await coordinatore.Partita.Task;
        coordinatore.Concludi();
        await prima.Completion;

        coordinatore.Current.Should().BeNull();
    }

    [Fact]
    public async Task Lo_slot_si_libera_anche_se_l_esecuzione_fallisce()
    {
        // È il caso che conta: senza il rilascio in `finally` un singolo errore bloccherebbe
        // ogni esecuzione successiva, notturna compresa, fino al riavvio del servizio.
        var coordinatore = new CoordinatoreGovernabile { Fallimento = new InvalidOperationException("Card Trader irraggiungibile") };

        var esecuzione = coordinatore.Start(Richiesta());
        await coordinatore.Partita.Task;
        coordinatore.Concludi();

        // L'eccezione non deve propagarsi a chi attende: il coordinatore la registra e basta.
        await esecuzione.Completion;

        coordinatore.Current.Should().BeNull();
        coordinatore.Start(Richiesta()).Started.Should().BeTrue();
    }

    [Fact]
    public async Task L_interruzione_annulla_il_token_dell_esecuzione_in_corso()
    {
        var coordinatore = new CoordinatoreGovernabile();

        var esecuzione = coordinatore.Start(Richiesta());
        await coordinatore.Partita.Task;

        coordinatore.RequestCancellation().Should().BeTrue();
        coordinatore.TokenRicevuto.IsCancellationRequested.Should().BeTrue();

        // L'annullamento si vede anche da fuori, perché l'interfaccia deve poter mostrare
        // "interruzione in corso": l'arresto non è istantaneo, avviene fra una carta e l'altra.
        coordinatore.Current!.CancellationRequested.Should().BeTrue();

        coordinatore.Concludi();
        await esecuzione.Completion;
        coordinatore.Current.Should().BeNull();
    }

    [Fact]
    public void L_interruzione_a_riposo_non_fa_nulla()
    {
        new CoordinatoreGovernabile().RequestCancellation().Should().BeFalse();
    }

    [Fact]
    public async Task Lo_stato_riporta_la_fase_scritta_dall_esecuzione()
    {
        // Durante la preparazione non esiste ancora una riga di storico da cui leggere i
        // contatori: la fase è l'unica cosa che distingue un lavoro lento da uno stallo.
        var coordinatore = new CoordinatoreGovernabile();

        var esecuzione = coordinatore.Start(Richiesta());
        await coordinatore.Partita.Task;

        coordinatore.Current!.Phase.Should().Be("In corso");
        coordinatore.Current!.RunId.Should().BeNull();

        coordinatore.Concludi();
        await esecuzione.Completion;
    }
}
