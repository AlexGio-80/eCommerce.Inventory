using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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

    private HoldStockService CreateService() =>
        new(_db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Purchasing:SellingCostPercent"] = "15" }).Build());

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
