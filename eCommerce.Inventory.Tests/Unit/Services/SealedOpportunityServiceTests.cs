using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>Classifica giornaliera delle opportunità sui sigillati (Fase 3).</summary>
public class SealedOpportunityServiceTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private readonly Guid _card = Guid.NewGuid();

    /// <summary>
    /// Uscita di prova: un box da 10 buste, ogni busta 1 carta da 20 €. Con bulk e fattore non
    /// toccati (carta sopra soglia, nessuna vendita recente = fattore 1) e costi al 15%, il valore
    /// atteso netto del box è 10 × 20 × 0,85 = 170 €.
    /// </summary>
    private async Task SeedReleaseAsync(decimal boxTrend)
    {
        _db.MtgjsonSets.Add(new MtgjsonSet { Code = "TST", Name = "Test Set", ReleaseDate = new DateOnly(2025, 1, 1), DetailImportedAt = DateTime.UtcNow, HasBoosterData = true });
        _db.MtgjsonSets.Add(new MtgjsonSet { Code = "OLD", Name = "Senza dati delle buste" });

        var pack = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Test Pack", Category = "booster_pack",
            Contents = { new SealedProductContent { Kind = SealedContentKind.Pack, PackCode = "play", SetCode = "TST" } } };
        var box = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Test Box", Category = "booster_box", CardmarketId = 1,
            Contents = { new SealedProductContent { Kind = SealedContentKind.Sealed, ChildUuid = pack.Uuid, Count = 10 } } };
        var boxCase = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Test Box Case", Category = "booster_case", CardmarketId = 2,
            Contents = { new SealedProductContent { Kind = SealedContentKind.Sealed, ChildUuid = box.Uuid, Count = 6 } } };
        var oldBox = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "OLD", Name = "Old Box", Category = "booster_box", CardmarketId = 3 };
        _db.SealedProducts.AddRange(pack, box, boxCase, oldBox);

        _db.BoosterConfigs.Add(new BoosterConfig { SetCode = "TST", BoosterType = "play", Weight = 1, TotalWeight = 1,
            Slots = { new BoosterConfigSlot { SheetName = "rare", Count = 1 } } });
        _db.BoosterSheets.Add(new BoosterSheet { SetCode = "TST", BoosterType = "play", Name = "rare", TotalWeight = 1,
            Cards = { new BoosterSheetCard { CardUuid = _card, Weight = 1 } } });
        _db.MtgjsonCards.Add(new MtgjsonCard { Uuid = _card, SetCode = "TST", Name = "Carta rara", CardmarketId = 10 });
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = 10, Trend = 20m });

        _db.CardmarketPriceSnapshots.AddRange(
            new CardmarketPriceSnapshot { IdProduct = 1, Date = Today, Trend = boxTrend },
            new CardmarketPriceSnapshot { IdProduct = 1, Date = Today.AddDays(-7), Trend = 200m },
            new CardmarketPriceSnapshot { IdProduct = 2, Date = Today, Trend = boxTrend * 6 },
            new CardmarketPriceSnapshot { IdProduct = 3, Date = Today, Trend = 99m });
        await _db.SaveChangesAsync();
    }

    private SealedOpportunityService CreateService()
    {
        var analysis = new SealedProductAnalysisService(_db, Mock.Of<ICardTraderApiService>(), new BulkSellThroughService(_db),
            new PriceRealizationService(_db), new ConfigurationBuilder().Build(), NullLogger<SealedProductAnalysisService>.Instance);
        return new SealedOpportunityService(_db, analysis, NullLogger<SealedOpportunityService>.Instance);
    }

    [Fact]
    public async Task Compute_StoresTodaysValue_ForReleasesWithBoosterData_WithoutCases()
    {
        await SeedReleaseAsync(boxTrend: 160m);

        var rows = await CreateService().ComputeAsync();

        var stored = await _db.SealedOpportunities.ToListAsync();
        stored.Should().ContainSingle("solo il box: la busta non ha prezzo, il case è nascosto e OLD non ha i dati delle buste");
        var box = stored.Single();
        box.Date.Should().Be(Today);
        box.OpenValueCm.Should().Be(170m);
        box.Decision.Should().Be("Apri", "170 € netti aprendo contro 136 € rivendendolo chiuso");
        rows.Should().Be(1);
    }

    [Fact]
    public async Task Compute_TwiceTheSameDay_ReplacesInsteadOfDuplicating()
    {
        await SeedReleaseAsync(boxTrend: 160m);
        var service = CreateService();

        await service.ComputeAsync();
        await service.ComputeAsync();

        (await _db.SealedOpportunities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetLatest_ReportsPriceAndValueChanges()
    {
        await SeedReleaseAsync(boxTrend: 160m);
        var service = CreateService();
        await service.ComputeAsync();
        var productId = (await _db.SealedOpportunities.SingleAsync()).SealedProductId;
        _db.SealedOpportunities.Add(new SealedOpportunity { Date = Today.AddDays(-7), SealedProductId = productId, MainSetCode = "TST", OpenValueCm = 200m });
        await _db.SaveChangesAsync();

        var list = await service.GetLatestAsync();

        var item = list.Items.Single();
        list.Date.Should().Be(Today);
        item.SetName.Should().Be("Test Set");
        item.TrendChange7.Should().Be(-20m, "da 200 a 160 €");
        item.ValueChange7.Should().Be(-15m, "valore atteso da 200 a 170 €");
        item.TrendChange30.Should().BeNull("non c'è il prezzo di 30 giorni fa");
    }
}
