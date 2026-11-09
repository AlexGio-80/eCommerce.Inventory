using eCommerce.Inventory.Infrastructure.Services;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// "Oggi conviene" nel registro acquisti: cosa fare di un acquisto ancora chiuso, sul prezzo pagato.
/// </summary>
public class PurchaseAdviceTests
{
    private static TodayDecision Today(string decision, decimal? open, decimal? sealedNet) =>
        new(decision, open, sealedNet, new DateOnly(2026, 11, 9));

    [Fact]
    public void Tieni_se_nessuna_strada_ripaga_il_prezzo()
    {
        // La classifica dice "Apri" (l'apertura rende più della rivendita), ma aprendo si perde.
        ProductPurchaseService.AdviceFor(Today("Apri", 185.22m, 163.87m), 203.07m).Should().Be("Tieni");
    }

    [Fact]
    public void Apri_appena_l_apertura_ripaga_il_prezzo()
    {
        ProductPurchaseService.AdviceFor(Today("Apri", 36.86m, 31.56m), 36.54m).Should().Be("Apri");
        ProductPurchaseService.AdviceFor(Today("Apri", 36.54m, 31.56m), 36.54m).Should().Be("Apri", "in pari basta");
    }

    [Fact]
    public void Vendi_sigillato_se_la_rivendita_ripaga_e_rende_di_piu()
    {
        ProductPurchaseService.AdviceFor(Today("Tieni sigillato", 80m, 95m), 90m).Should().Be("Vendi sigillato");
    }

    [Fact]
    public void Apri_se_ripagano_tutte_e_due_ma_l_apertura_rende_di_piu()
    {
        ProductPurchaseService.AdviceFor(Today("Apri", 120m, 100m), 90m).Should().Be("Apri");
    }

    [Theory]
    [InlineData("Dati incompleti")]
    [InlineData("Prezzo CM dubbio")]
    [InlineData("Non giocabili")]
    public void Le_decisioni_senza_valore_affidabile_restano_come_sono(string decision)
    {
        ProductPurchaseService.AdviceFor(Today(decision, 500m, 500m), 10m).Should().Be(decision);
    }

    [Fact]
    public void Senza_classifica_nessuna_indicazione()
    {
        ProductPurchaseService.AdviceFor(null, 10m).Should().BeNull();
    }
}
