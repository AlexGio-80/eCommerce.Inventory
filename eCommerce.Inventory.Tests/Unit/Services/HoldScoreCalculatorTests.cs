using eCommerce.Inventory.Infrastructure.Services;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Il punteggio "da tenere" decide quali prodotti arrivano negli avvisi: i casi limite di ogni
/// componente (pavimento, età, storico) e i prodotti che non vanno valutati affatto.
/// </summary>
public class HoldScoreCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly HoldSettings Settings = new();

    private static HoldInput Input(
        string category = "booster_box", string? subtype = "draft", string decision = "Tieni sigillato",
        decimal openValue = 130m, decimal price = 155m, DateOnly? release = null,
        decimal? min = null, decimal? max = null, int historyDays = 8) =>
        new(category, subtype, decision, openValue, price, price, release ?? new DateOnly(2024, 10, 1), Today, min, max, historyDays);

    [Theory]
    [InlineData(60, 0)]
    [InlineData(50, 0)]
    [InlineData(80, 50)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void FloorPoints_AreLinearBetween60And100Percent(double floor, double expected) =>
        HoldScoreCalculator.FloorPoints((decimal)floor).Should().Be((decimal)expected);

    [Theory]
    [InlineData(3, 0)]     // il prezzo di solito sta ancora scendendo
    [InlineData(9, 50)]
    [InlineData(12, 100)]
    [InlineData(30, 100)]
    [InlineData(45, 50)]
    [InlineData(60, 0)]    // fuori stampa da anni: il rialzo di solito c'è già stato
    [InlineData(-2, 0)]    // preordine
    public void AgePoints_PeakBetween12And30Months(int months, double expected) =>
        HoldScoreCalculator.AgePoints(months).Should().Be((decimal)expected);

    [Fact]
    public void AgeInMonths_CountsCompletedMonths()
    {
        HoldScoreCalculator.AgeInMonths(new DateOnly(2025, 10, 15), Today).Should().Be(12);
        HoldScoreCalculator.AgeInMonths(new DateOnly(2025, 10, 16), Today).Should().Be(11);
        HoldScoreCalculator.AgeInMonths(new DateOnly(2026, 11, 13), Today).Should().Be(-1);
    }

    [Fact]
    public void HistoryPosition_IsNeutral_UntilTheHistoryIsLongEnough()
    {
        HoldScoreCalculator.HistoryPosition(100m, 90m, 120m, historyDays: 8, minHistoryDays: 60).Should().BeNull();
        HoldScoreCalculator.HistoryPosition(90m, 90m, 120m, historyDays: 90, minHistoryDays: 60).Should().Be(100m, "al minimo");
        HoldScoreCalculator.HistoryPosition(120m, 90m, 120m, historyDays: 90, minHistoryDays: 60).Should().Be(0m, "al massimo");
        HoldScoreCalculator.HistoryPosition(100m, 100m, 100m, historyDays: 90, minHistoryDays: 60).Should().Be(50m, "prezzo mai cambiato");
    }

    [Fact]
    public void Compute_BoxTwoYearsOld_WithCardsWorthMostOfThePrice_IsACandidate()
    {
        // Le carte valgono l'84% del prezzo, uscita di 24 mesi fa, storico ancora corto.
        var score = HoldScoreCalculator.Compute(Input(openValue: 130m, price: 155m), Settings)!;

        score.FloorPercent.Should().Be(83.9m);
        score.AgeMonths.Should().Be(24);
        score.HistoryPosition.Should().BeNull();
        // 50% × 59,75 + 30% × 100 + 20% × 50 (neutro)
        score.Score.Should().Be(69.9m);
        score.IsCandidate.Should().BeTrue();
    }

    [Fact]
    public void Compute_RecentRelease_IsNotACandidate_EvenWithAGoodFloor()
    {
        var score = HoldScoreCalculator.Compute(Input(openValue: 150m, price: 150m, release: new DateOnly(2026, 7, 1)), Settings)!;

        score.AgeMonths.Should().Be(3);
        score.IsCandidate.Should().BeFalse("sotto i 6 mesi il prezzo di solito sta ancora scendendo");
    }

    [Fact]
    public void Compute_ProductThatAlreadyPaysToOpen_IsNotACandidate()
    {
        // Rende già aprendolo: è un'occasione "compra e apri", nella lista Apri e nei suoi avvisi.
        var input = Input(decision: "Apri", openValue: 170m, price: 160m) with { OpeningRoiPercent = 6.3m };

        HoldScoreCalculator.Compute(input, Settings)!.IsCandidate.Should().BeFalse();
        HoldScoreCalculator.Compute(input with { OpeningRoiPercent = -3.8m, OpenValueNet = 154m }, Settings)!.IsCandidate.Should().BeTrue();
    }

    [Fact]
    public void Compute_LowFloor_IsNotACandidate()
    {
        var score = HoldScoreCalculator.Compute(Input(openValue: 100m, price: 155m), Settings)!;

        score.FloorPercent.Should().Be(64.5m);
        score.IsCandidate.Should().BeFalse();
    }

    [Theory]
    [InlineData("secret_lair")]
    [InlineData("secret_lair_bundle")]
    public void Compute_SkipsSecretLair(string subtype) =>
        // L'utente li apre sempre e li segue nella loro scheda.
        HoldScoreCalculator.Compute(Input(category: "box_set", subtype: subtype, openValue: 38m, price: 40m), Settings).Should().BeNull();

    [Fact]
    public void Compute_WithoutReleaseDate_CountsAgeAsNeutral()
    {
        var score = HoldScoreCalculator.Compute(Input(openValue: 150m, price: 155m) with { ReleaseDate = null }, Settings)!;

        score.AgeMonths.Should().BeNull();
        score.IsCandidate.Should().BeTrue("un'età non nota non esclude il prodotto");
    }

    [Theory]
    [InlineData("booster_pack", "Tieni sigillato")]   // tipo non da tenere
    [InlineData("limited_aid_tool", "Apri")]
    [InlineData("booster_box", "Prezzo CM dubbio")]   // prezzo non affidabile
    [InlineData("booster_box", "Nessuna offerta")]
    [InlineData("booster_box", "Dati incompleti")]
    public void Compute_SkipsProductsThatCannotBeScored(string category, string decision) =>
        HoldScoreCalculator.Compute(Input(category: category, decision: decision), Settings).Should().BeNull();
}
