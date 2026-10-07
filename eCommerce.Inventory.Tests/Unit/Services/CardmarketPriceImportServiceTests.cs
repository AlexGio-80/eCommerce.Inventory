using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.Cardmarket;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// L'import del listino Cardmarket è l'unica fonte dello storico prezzi dei sigillati: un giorno
/// saltato o scritto male non si recupera più.
/// </summary>
public class CardmarketPriceImportServiceTests
{
    private const int PlayBox = 897524;
    private const int CollectorBox = 897523;
    private const int NewSingle = 900001;
    private const int NewSingle2 = 900002;
    private const int OldSingle = 100001;

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Mock<ICardmarketDownloadClient> _client = new();

    private CardmarketPriceImportService CreateService() => new(
        _db,
        _client.Object,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CardmarketImport:SinglesTrackingMonths"] = "12" })
            .Build(),
        NullLogger<CardmarketPriceImportService>.Instance);

    private void SetupCatalogs()
    {
        _client.Setup(c => c.GetSealedProductsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardmarketProductListFile
            {
                Products =
                {
                    Catalog(PlayBox, "Star Trek Play Booster Box", "Magic Display", 6000, "2026-07-15 13:18:14"),
                    Catalog(CollectorBox, "Star Trek Collector Booster Box", "Magic Display", 6000, "2026-07-15 13:15:58")
                }
            });

        _client.Setup(c => c.GetSingleProductsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardmarketProductListFile
            {
                Products =
                {
                    Catalog(NewSingle, "Kirk", "Magic Single", 6001, "2026-08-01 10:00:00"),
                    Catalog(NewSingle2, "Spock", "Magic Single", 6001, "2026-09-01 10:00:00"),
                    // Espansione vecchia: la ristampa aggiunta di recente non la fa seguire.
                    Catalog(OldSingle, "Lightning Bolt", "Magic Single", 10, "2008-01-01 00:00:00"),
                    Catalog(OldSingle + 1, "Lightning Bolt", "Magic Single", 10, "2026-09-30 00:00:00")
                }
            });
    }

    private void SetupPriceGuide(string createdAt, DateTimeOffset lastModified, params CardmarketPriceGuideEntry[] prices)
    {
        _client.Setup(c => c.GetPriceGuideLastModifiedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lastModified);
        _client.Setup(c => c.GetPriceGuideAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CardmarketPriceGuideFile { CreatedAt = createdAt, PriceGuides = prices.ToList() });
    }

    private static CardmarketCatalogEntry Catalog(int id, string name, string category, int expansion, string dateAdded) =>
        new() { IdProduct = id, Name = name, CategoryName = category, IdExpansion = expansion, DateAdded = dateAdded };

    private static CardmarketPriceGuideEntry Price(int id, decimal trend, decimal low = 1m, decimal? avg7 = null, decimal? trendFoil = 0m) =>
        new() { IdProduct = id, Trend = trend, Low = low, Avg = trend, Avg7 = avg7, TrendFoil = trendFoil };

    [Fact]
    public async Task FirstImport_WritesEverySealedAndEveryTrackedSingle()
    {
        SetupCatalogs();
        SetupPriceGuide("2026-10-07T02:49:35+0200", DateTimeOffset.Parse("2026-10-07T00:49:47Z"),
            Price(PlayBox, 140.35m), Price(CollectorBox, 428.32m),
            Price(NewSingle, 2.50m, trendFoil: 5m), Price(NewSingle2, 0.10m), Price(OldSingle, 3m));

        var log = await CreateService().ImportAsync(CardmarketImportTrigger.Manual);

        log.Outcome.Should().Be(CardmarketImportOutcome.Succeeded);
        log.SealedSnapshotsWritten.Should().Be(2);
        log.SinglesTracked.Should().Be(2);
        log.SinglesSnapshotsWritten.Should().Be(2);
        log.NewProducts.Should().Be(4);

        var snapshots = await _db.CardmarketPriceSnapshots.ToListAsync();
        snapshots.Select(s => s.IdProduct).Should().BeEquivalentTo(new[] { PlayBox, CollectorBox, NewSingle, NewSingle2 });
        snapshots.Should().OnlyContain(s => s.Date == new DateOnly(2026, 10, 7),
            "il giorno è quello del listino nel suo fuso, non quello UTC");
        snapshots.Single(s => s.IdProduct == PlayBox).TrendFoil.Should().BeNull("i sigillati non hanno prezzi foil");
        snapshots.Single(s => s.IdProduct == NewSingle).TrendFoil.Should().Be(5m);

        (await _db.CardmarketProducts.SingleAsync(p => p.IdProduct == PlayBox)).IsSingle.Should().BeFalse();
        (await _db.CardmarketProducts.SingleAsync(p => p.IdProduct == NewSingle)).IsSingle.Should().BeTrue();
    }

    [Fact]
    public async Task NextDay_WritesAllSealedAgain_ButOnlyTheSinglesThatChanged()
    {
        SetupCatalogs();
        SetupPriceGuide("2026-10-07T02:49:35+0200", DateTimeOffset.Parse("2026-10-07T00:49:47Z"),
            Price(PlayBox, 140m), Price(CollectorBox, 428m), Price(NewSingle, 2.50m, avg7: 2m), Price(NewSingle2, 0.10m));
        await CreateService().ImportAsync(CardmarketImportTrigger.Scheduled);

        // Il giorno dopo: box invariati, Kirk cambia solo la media a 7 giorni, Spock cambia trend.
        SetupPriceGuide("2026-10-08T02:50:00+0200", DateTimeOffset.Parse("2026-10-08T00:50:00Z"),
            Price(PlayBox, 140m), Price(CollectorBox, 428m), Price(NewSingle, 2.50m, avg7: 2.2m), Price(NewSingle2, 0.12m));
        var log = await CreateService().ImportAsync(CardmarketImportTrigger.Scheduled);

        log.SealedSnapshotsWritten.Should().Be(2, "i sigillati hanno una riga al giorno anche a prezzo fermo");
        log.SinglesSnapshotsWritten.Should().Be(1, "la sola media a 7 giorni non è una variazione");

        var secondDay = await _db.CardmarketPriceSnapshots.Where(s => s.Date == new DateOnly(2026, 10, 8)).ToListAsync();
        secondDay.Select(s => s.IdProduct).Should().BeEquivalentTo(new[] { PlayBox, CollectorBox, NewSingle2 });
    }

    [Fact]
    public async Task SameDayGuideAgain_IsSkippedWithoutDuplicatingTheSeries()
    {
        SetupCatalogs();
        SetupPriceGuide("2026-10-07T02:49:35+0200", DateTimeOffset.Parse("2026-10-07T00:49:47Z"), Price(PlayBox, 140m));
        await CreateService().ImportAsync(CardmarketImportTrigger.Scheduled);

        var log = await CreateService().ImportAsync(CardmarketImportTrigger.Manual, force: true);

        log.Outcome.Should().Be(CardmarketImportOutcome.Skipped);
        (await _db.CardmarketPriceSnapshots.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task UnchangedLastModified_IsSkippedWithoutDownloadingTheGuide()
    {
        SetupCatalogs();
        var lastModified = DateTimeOffset.Parse("2026-10-07T00:49:47Z");
        SetupPriceGuide("2026-10-07T02:49:35+0200", lastModified, Price(PlayBox, 140m));
        await CreateService().ImportAsync(CardmarketImportTrigger.Scheduled);
        _client.Invocations.Clear();

        var log = await CreateService().ImportAsync(CardmarketImportTrigger.Startup);

        log.Outcome.Should().Be(CardmarketImportOutcome.Skipped);
        _client.Verify(c => c.GetPriceGuideAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadFailure_IsRecordedAsFailed_AndDoesNotThrow()
    {
        _client.Setup(c => c.GetPriceGuideLastModifiedAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));

        var log = await CreateService().ImportAsync(CardmarketImportTrigger.Scheduled);

        log.Outcome.Should().Be(CardmarketImportOutcome.Failed);
        log.Message.Should().Contain("503");
        (await _db.CardmarketImportLogs.SingleAsync()).Outcome.Should().Be(CardmarketImportOutcome.Failed);
    }

    [Fact]
    public void PriceGuideCreatedAt_WithOffsetWithoutColon_IsParsed()
    {
        var file = new CardmarketPriceGuideFile { CreatedAt = "2026-10-07T02:49:35+0200" };

        file.GetCreatedAt().Should().Be(new DateTimeOffset(2026, 10, 7, 2, 49, 35, TimeSpan.FromHours(2)));
    }
}
