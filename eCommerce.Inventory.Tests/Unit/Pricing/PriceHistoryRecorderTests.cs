using eCommerce.Inventory.Application.Pricing;
using FluentAssertions;
using Xunit;

using Observation = eCommerce.Inventory.Application.Pricing.PriceHistoryRecorder.Observation;
using PreviousState = eCommerce.Inventory.Application.Pricing.PriceHistoryRecorder.PreviousState;

namespace eCommerce.Inventory.Tests.Unit.Pricing;

/// <summary>
/// Lo storico è a delta: scrivere ogni notte tutte le 35.000 inserzioni produrrebbe milioni di
/// righe l'anno per rappresentare in gran parte prezzi fermi. Per ricostruire un andamento basta
/// sapere quando è cambiato — purché ogni serie abbia un punto di partenza.
/// </summary>
public class PriceHistoryRecorderTests
{
    private static readonly DateTime Adesso = new(2026, 8, 29, 1, 0, 0, DateTimeKind.Utc);

    private static Observation Osservazione(decimal price, int quantity = 1, int productId = 100)
        => new(productId, BlueprintId: 10, InventoryItemId: 55,
               price, quantity, "Near Mint", "English", IsFoil: false);

    [Fact]
    public void Registra_quando_il_prezzo_e_cambiato()
    {
        var precedente = new Dictionary<int, PreviousState> { [100] = new(19.99m, 1) };

        var entries = PriceHistoryRecorder.SelectEntriesToRecord(
            new[] { Osservazione(21.50m) }, precedente, Adesso);

        entries.Should().ContainSingle();
        entries[0].Price.Should().Be(21.50m);
        entries[0].RecordedAt.Should().Be(Adesso);
    }

    [Fact]
    public void Registra_quando_cambia_solo_la_quantita()
    {
        // Il prezzo fermo ma la quantità che scende racconta una vendita, ed è il contesto che
        // spiega i movimenti di prezzo successivi.
        var precedente = new Dictionary<int, PreviousState> { [100] = new(19.99m, 10) };

        var entries = PriceHistoryRecorder.SelectEntriesToRecord(
            new[] { Osservazione(19.99m, quantity: 9) }, precedente, Adesso);

        entries.Should().ContainSingle();
        entries[0].Quantity.Should().Be(9);
    }

    [Fact]
    public void Non_registra_nulla_se_prezzo_e_quantita_sono_fermi()
    {
        var precedente = new Dictionary<int, PreviousState> { [100] = new(19.99m, 3) };

        var entries = PriceHistoryRecorder.SelectEntriesToRecord(
            new[] { Osservazione(19.99m, quantity: 3) }, precedente, Adesso);

        entries.Should().BeEmpty("un prezzo fermo non aggiunge informazione alla serie");
    }

    [Fact]
    public void Registra_il_primo_punto_di_una_inserzione_mai_vista()
    {
        // Senza stato precedente la riga va scritta comunque: una serie che parte dal primo
        // cambiamento non avrebbe un valore iniziale da cui far partire la linea, e un'inserzione
        // dal prezzo stabile sarebbe indistinguibile da una di cui non si sa nulla.
        var entries = PriceHistoryRecorder.SelectEntriesToRecord(
            new[] { Osservazione(19.99m) },
            new Dictionary<int, PreviousState>(),
            Adesso);

        entries.Should().ContainSingle();
        entries[0].Price.Should().Be(19.99m);
    }

    [Fact]
    public void Conserva_le_caratteristiche_della_versione()
    {
        // Denormalizzate perché la riga resti leggibile anche dopo che l'inserzione è stata
        // cancellata: senza, una serie per carta mescolerebbe la foil con la non foil.
        var osservazione = new Observation(
            CardTraderProductId: 200, BlueprintId: 42, InventoryItemId: null,
            Price: 140.37m, Quantity: 1, Condition: "Near Mint", Language: "English", IsFoil: true);

        var entries = PriceHistoryRecorder.SelectEntriesToRecord(
            new[] { osservazione }, new Dictionary<int, PreviousState>(), Adesso);

        entries.Should().ContainSingle();
        entries[0].CardTraderProductId.Should().Be(200);
        entries[0].BlueprintId.Should().Be(42);
        entries[0].InventoryItemId.Should().BeNull();
        entries[0].IsFoil.Should().BeTrue();
        entries[0].Condition.Should().Be("Near Mint");
    }

    [Fact]
    public void Tiene_solo_le_inserzioni_effettivamente_cambiate()
    {
        var precedente = new Dictionary<int, PreviousState>
        {
            [100] = new(19.99m, 1),   // ferma
            [101] = new(50.00m, 1)    // cambiata
        };

        var entries = PriceHistoryRecorder.SelectEntriesToRecord(
            new[]
            {
                Osservazione(19.99m, productId: 100),
                Osservazione(55.00m, productId: 101),
                Osservazione(3.00m, productId: 102)   // mai vista
            },
            precedente, Adesso);

        entries.Select(e => e.CardTraderProductId).Should().BeEquivalentTo(new[] { 101, 102 });
    }
}
