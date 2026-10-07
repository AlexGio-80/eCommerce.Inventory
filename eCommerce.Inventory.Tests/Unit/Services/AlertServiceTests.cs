using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>Avvisi sugli acquisti (Fase 4): devono scattare una volta, quando la condizione diventa vera.</summary>
public class AlertServiceTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Mock<IEmailSender> _email = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private AlertService CreateService() => new(_db, _email.Object, NullLogger<AlertService>.Instance);

    private async Task<SealedProduct> SeedBoxAsync(decimal trend, decimal? trendWeekAgo = null)
    {
        _db.MtgjsonSets.Add(new MtgjsonSet { Code = "TRK", Name = "Star Trek" });
        var box = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TRK", Name = "Star Trek Play Booster Box", Category = "booster_box", CardmarketId = 897524 };
        _db.SealedProducts.Add(box);
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = 897524, Trend = trend, Low = trend - 5 });
        if (trendWeekAgo.HasValue)
            _db.CardmarketPriceSnapshots.Add(new CardmarketPriceSnapshot { IdProduct = 897524, Date = Today.AddDays(-7), Trend = trendWeekAgo });
        await _db.SaveChangesAsync();
        return box;
    }

    private async Task SetTrendAsync(decimal trend)
    {
        var price = await _db.CardmarketLatestPrices.SingleAsync();
        price.Trend = trend;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task PriceBelow_FiresOnce_WhileTheConditionStaysTrue_AndAgainAfterItWasFalse()
    {
        var box = await SeedBoxAsync(trend: 140.35m);
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Play Box TRK sotto 130", AlertRuleType.PriceBelow, box.Id, null, null, 130m, false, true, false));

        (await service.EvaluateAsync()).NewNotifications.Should().Be(0, "140,35 € non è sotto 130");

        await SetTrendAsync(128m);
        (await service.EvaluateAsync()).NewNotifications.Should().Be(1);
        (await service.EvaluateAsync()).NewNotifications.Should().Be(0, "la condizione era già vera: niente avvisi ripetuti");

        await SetTrendAsync(135m);
        await service.EvaluateAsync();
        await SetTrendAsync(125m);
        (await service.EvaluateAsync()).NewNotifications.Should().Be(1, "è tornata vera dopo essere stata falsa");

        var notification = (await service.ListNotificationsAsync(10)).Items.First();
        notification.Title.Should().Be("Play Box TRK sotto 130: Star Trek Play Booster Box");
        notification.SetCode.Should().Be("TRK");
        notification.Message.Should().Contain("125,00");
    }

    [Fact]
    public async Task PriceDrop_ComparesWithTheTrendOfSevenDaysAgo()
    {
        var box = await SeedBoxAsync(trend: 126m, trendWeekAgo: 140m);
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Calo Play Box", AlertRuleType.PriceDrop, box.Id, null, null, 10m, false, true, false));

        var result = await service.EvaluateAsync();

        result.NewNotifications.Should().Be(1, "da 140 a 126 € è un calo del 10%");
        (await service.ListNotificationsAsync(10)).Items.Single().Message.Should().Contain("10,0%");
    }

    [Fact]
    public async Task OpeningOpportunity_FiltersByReleaseAndCategory()
    {
        var box = await SeedBoxAsync(trend: 140m);
        var deck = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TRC", Name = "Commander Deck", Category = "deck" };
        _db.SealedProducts.Add(deck);
        _db.SealedOpportunities.AddRange(
            new SealedOpportunity { Date = Today, SealedProductId = box.Id, MainSetCode = "TRK", Decision = "Apri", OpeningRoiPercent = 25m, OpenValueCm = 175m, CmTrend = 140m },
            new SealedOpportunity { Date = Today, SealedProductId = deck.Id, MainSetCode = "TRK", Decision = "Apri", OpeningRoiPercent = 60m, OpenValueCm = 80m, CmTrend = 50m });
        await _db.SaveChangesAsync();
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Box TRK da aprire", AlertRuleType.OpeningOpportunity, null, "trk", "booster_box", 20m, false, true, false));

        (await service.EvaluateAsync()).NewNotifications.Should().Be(1, "il mazzo è escluso dal filtro sulla categoria");
    }

    [Fact]
    public async Task Email_IsOneDigestPerRun_AndAFailureIsRecordedOnTheNotifications()
    {
        var box = await SeedBoxAsync(trend: 100m, trendWeekAgo: 200m);
        _email.SetupGet(e => e.IsConfigured).Returns(true);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP non raggiungibile"));
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Sotto 130", AlertRuleType.PriceBelow, box.Id, null, null, 130m, false, true, true));
        await service.SaveRuleAsync(null, new AlertRuleInput("Calo", AlertRuleType.PriceDrop, box.Id, null, null, 20m, false, true, true));

        var result = await service.EvaluateAsync();

        result.NewNotifications.Should().Be(2);
        result.Email.Should().Be("fallita");
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        (await service.ListNotificationsAsync(10)).Items.Should().OnlyContain(n => n.EmailError!.Contains("SMTP non raggiungibile"));
    }

    private async Task<SealedProduct> AddBoxAsync(string set, DateOnly? release, string subtype, int cmId, decimal trend, params (int DaysAgo, decimal Trend)[] history)
    {
        if (!await _db.MtgjsonSets.AnyAsync(s => s.Code == set))
            _db.MtgjsonSets.Add(new MtgjsonSet { Code = set, Name = set, ReleaseDate = release });
        var box = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = set, Name = $"{set} {subtype} box", Category = "booster_box", Subtype = subtype, CardmarketId = cmId };
        _db.SealedProducts.Add(box);
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = cmId, Trend = trend });
        foreach (var (daysAgo, value) in history)
            _db.CardmarketPriceSnapshots.Add(new CardmarketPriceSnapshot { IdProduct = cmId, Date = Today.AddDays(-daysAgo), Trend = value });
        await _db.SaveChangesAsync();
        return box;
    }

    [Fact]
    public async Task GenericRule_CoversEveryProductInScope_WithOneNotificationForAllOfThem()
    {
        await AddBoxAsync("AAA", new DateOnly(2026, 1, 1), "collector", 1, 300m, (7, 400m));
        await AddBoxAsync("BBB", new DateOnly(2025, 6, 1), "collector", 2, 450m, (7, 500m));
        await AddBoxAsync("CCC", new DateOnly(2026, 2, 1), "play", 3, 100m, (7, 150m));
        _db.SealedProducts.Add(new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "AAA", Name = "AAA collector case", Category = "booster_case", Subtype = "collector", CardmarketId = 4 });
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = 4, Trend = 10m });
        _db.CardmarketPriceSnapshots.Add(new CardmarketPriceSnapshot { IdProduct = 4, Date = Today.AddDays(-7), Trend = 100m });
        await _db.SaveChangesAsync();
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Collector in calo", AlertRuleType.PriceDrop, null, null, "booster_box", 10m, false, true, false, Subtype: "collector"));

        var result = await service.EvaluateAsync();

        result.NewNotifications.Should().Be(1, "un avviso per regola, con l'elenco dei prodotti");
        var notification = (await service.ListNotificationsAsync(10)).Items.Single();
        notification.Title.Should().Be("Collector in calo: 2 prodotti", "AAA −25% e BBB −10%; il play box e il case sono fuori ambito");
        notification.Message.Should().StartWith("• AAA collector box", "il calo più forte per primo");
        notification.SetCode.Should().BeNull("due uscite diverse");
        (await service.ListRulesAsync()).Single().MatchingCount.Should().Be(2);
    }

    [Fact]
    public async Task RecentReleaseFilter_KeepsOnlyRecentAndUpcomingReleases()
    {
        await AddBoxAsync("OLD", Today.AddDays(-400), "play", 1, 100m, (7, 200m));
        await AddBoxAsync("NEW", Today.AddDays(-30), "play", 2, 100m, (7, 200m));
        await AddBoxAsync("PRE", Today.AddDays(30), "play", 3, 100m, (7, 200m));
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Recenti in calo", AlertRuleType.PriceDrop, null, null, "booster_box", 10m, false, true, false, RecentReleaseDays: 180));

        await service.EvaluateAsync();

        (await service.ListRulesAsync()).Single().MatchingCount.Should().Be(2, "uscita di un mese fa e preordine; quella di 400 giorni fa no");
    }

    [Fact]
    public async Task PriceAtLow_FiresOnlyWhenTheHistoryCoversTheWholeWindow()
    {
        // Prodotto con storico di 35 giorni: oggi 90 €, minimo precedente 95 €
        await AddBoxAsync("LOW", Today.AddDays(-60), "play", 1, 90m, (35, 120m), (20, 95m), (1, 100m));
        // Prodotto comparso da 3 giorni: il suo "minimo" non vuol dire niente
        await AddBoxAsync("NEW", Today.AddDays(-3), "play", 2, 50m, (3, 80m));
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Al minimo di 30 giorni", AlertRuleType.PriceAtLow, null, null, "booster_box", 30m, false, true, false));

        await service.EvaluateAsync();

        var notification = (await service.ListNotificationsAsync(10)).Items.Single();
        notification.Title.Should().Be("Al minimo di 30 giorni: LOW play box");
        notification.Message.Should().Contain("95,00");
    }

    [Fact]
    public async Task PriceAtLow_IsRejectedWithTooShortAWindow()
    {
        var save = () => CreateService().SaveRuleAsync(null, new AlertRuleInput("Minimo di 3 giorni", AlertRuleType.PriceAtLow, null, null, null, 3m, false, true, false));

        await save.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task MarkRead_WithoutId_MarksEverything()
    {
        var box = await SeedBoxAsync(trend: 100m);
        var service = CreateService();
        await service.SaveRuleAsync(null, new AlertRuleInput("Sotto 130", AlertRuleType.PriceBelow, box.Id, null, null, 130m, false, true, false));
        await service.EvaluateAsync();

        (await service.ListNotificationsAsync(10)).Unread.Should().Be(1);
        await service.MarkReadAsync(null);

        (await service.ListNotificationsAsync(10)).Unread.Should().Be(0);
    }
}
