using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Magazzino da tenere: quanto vale oggi ciò che è stato comprato per tenerlo, e quali acquisti ne
/// fanno parte. Prezzi e quantità sono di fantasia (repository pubblico).
/// </summary>
public class HoldStockServiceTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private readonly Mock<ICardTraderApiService> _cardTrader = new();

    private HoldStockService CreateService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Purchasing:SellingCostPercent"] = "15" }).Build();
        var analysis = new SealedProductAnalysisService(_db, _cardTrader.Object, new BulkSellThroughService(_db),
            new PriceRealizationService(_db), configuration, NullLogger<SealedProductAnalysisService>.Instance);
        return new HoldStockService(_db, configuration, analysis);
    }

    [Fact]
    public async Task RefreshCardTraderPrices_OnlyForStaleProductsStillHeld()
    {
        var stale = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Da aggiornare", CardTraderBlueprintId = 501 };
        var fresh = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Aggiornato da poco", CardTraderBlueprintId = 502,
            CtMinPrice = 50m, CtPriceUpdatedAt = DateTime.UtcNow.AddHours(-1) };
        var notHeld = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Non da tenere", CardTraderBlueprintId = 503 };
        _db.SealedProducts.AddRange(stale, fresh, notHeld);
        await _db.SaveChangesAsync();
        _db.ProductPurchases.AddRange(
            new ProductPurchase { SealedProductId = stale.Id, Quantity = 1, UnitPrice = 40m, HoldForResale = true },
            new ProductPurchase { SealedProductId = fresh.Id, Quantity = 1, UnitPrice = 40m, HoldForResale = true },
            new ProductPurchase { SealedProductId = notHeld.Id, Quantity = 1, UnitPrice = 40m });
        await _db.SaveChangesAsync();
        _cardTrader.Setup(c => c.GetMarketplaceProductsAsync(501, It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new CardTraderMarketplaceProductDto { BlueprintId = 501, PriceCents = 4599, Quantity = 1 },
            new CardTraderMarketplaceProductDto { BlueprintId = 501, PriceCents = 4250, Quantity = 2 },
            new CardTraderMarketplaceProductDto { BlueprintId = 501, PriceCents = 3000, Quantity = 0 } // esaurita
        });

        var updated = await CreateService().RefreshCardTraderPricesAsync();

        updated.Should().Be(1);
        (await _db.SealedProducts.FindAsync(stale.Id))!.CtMinPrice.Should().Be(42.50m);
        (await _db.SealedProducts.FindAsync(stale.Id))!.CtOfferCount.Should().Be(2);
        _cardTrader.Verify(c => c.GetMarketplaceProductsAsync(It.Is<int>(id => id != 501), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Value_TakesTheBetterExit_NetOfSellingCosts()
    {
        // 2 box a 100 €: chiusi valgono 120 × 0,85 = 102 € netti l'uno, aperti 95 €.
        var value = HoldStockService.Value(2, 100m, trend: 120m, sealedNet: null, openNet: 95m, costPercent: 15m);

        value.Cost.Should().Be(200m);
        value.SealedNetUnit.Should().Be(102m);
        value.BestExit.Should().Be(HoldExit.Sealed);
        value.ValueTotal.Should().Be(204m);
        value.Gain.Should().Be(4m);
        value.GainPercent.Should().Be(2m);
    }

    [Fact]
    public void Value_OpeningCanBeTheBetterExit()
    {
        var value = HoldStockService.Value(1, 100m, trend: 110m, sealedNet: 93.5m, openNet: 130m, costPercent: 15m);

        value.BestExit.Should().Be(HoldExit.Open);
        value.Gain.Should().Be(30m);
    }

    [Fact]
    public void Value_WithoutPrices_HasNoGain()
    {
        var value = HoldStockService.Value(1, 100m, trend: null, sealedNet: null, openNet: null, costPercent: 15m);

        value.Cost.Should().Be(100m);
        value.ValueTotal.Should().BeNull();
        value.Gain.Should().BeNull();
    }

    [Fact]
    public void TrendOn_TakesTheLastPriceUpToThatDay_OrNothingBeforeTheHistory()
    {
        var series = new List<(DateOnly, decimal)> { (new DateOnly(2026, 10, 7), 100m), (new DateOnly(2026, 10, 10), 110m) };

        HoldStockService.TrendOn(series, new DateOnly(2026, 10, 9)).Should().Be(100m);
        HoldStockService.TrendOn(series, new DateOnly(2026, 10, 15)).Should().Be(110m);
        HoldStockService.TrendOn(series, new DateOnly(2026, 9, 1)).Should().BeNull("lo storico parte dopo");
    }

    [Fact]
    public async Task Get_ListsOnlyPurchasesToHold_StillSealed_WithTotals()
    {
        var box = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Box da tenere", Category = "booster_box", CardmarketId = 1, CtMinPrice = 125m };
        var deck = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "TST", Name = "Mazzo", Category = "deck", CardmarketId = 2 };
        _db.SealedProducts.AddRange(box, deck);
        await _db.SaveChangesAsync();

        _db.ProductPurchases.AddRange(
            new ProductPurchase { SealedProductId = box.Id, Quantity = 2, UnitPrice = 100m, PurchasedAt = Today.AddDays(-5), HoldForResale = true },
            // Aperto: non è più in magazzino.
            new ProductPurchase { SealedProductId = box.Id, Quantity = 1, UnitPrice = 100m, HoldForResale = true, OpenedAt = Today },
            // Comprato per aprirlo: non è "da tenere".
            new ProductPurchase { SealedProductId = deck.Id, Quantity = 1, UnitPrice = 40m });
        _db.SealedOpportunities.Add(new SealedOpportunity
        {
            Date = Today, SealedProductId = box.Id, MainSetCode = "TST", CmTrend = 120m, SealedNetCm = 102m, OpenValueCm = 95m, Decision = "Tieni sigillato"
        });
        _db.CardmarketPriceSnapshots.Add(new CardmarketPriceSnapshot { IdProduct = 1, Date = Today.AddDays(-6), Trend = 110m });
        await _db.SaveChangesAsync();

        var stock = await CreateService().GetAsync();

        var item = stock.Items.Should().ContainSingle().Subject;
        item.Name.Should().Be("Box da tenere");
        item.CtMinPrice.Should().Be(125m);
        item.Gain.Should().Be(4m, "2 × 102 € netti contro 200 € spesi");
        item.TrendAtPurchase.Should().Be(110m);
        item.TrendChangeSincePurchase.Should().Be(9.1m, "da 110 a 120 €");
        stock.TotalCost.Should().Be(200m);
        stock.TotalGain.Should().Be(4m);
    }
}
