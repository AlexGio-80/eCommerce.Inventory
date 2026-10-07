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

    [Fact]
    public async Task PriceRule_WithoutProduct_IsRejected()
    {
        var service = CreateService();

        var save = () => service.SaveRuleAsync(null, new AlertRuleInput("Senza prodotto", AlertRuleType.PriceBelow, null, null, null, 10m, false, true, false));

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
