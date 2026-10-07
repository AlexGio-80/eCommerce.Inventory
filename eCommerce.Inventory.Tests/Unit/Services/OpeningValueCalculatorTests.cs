using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Il valore atteso decide se aprire o tenere sigillato: i casi sono costruiti con numeri tondi
/// verificabili a mano.
/// </summary>
public class OpeningValueCalculatorTests
{
    private static readonly Guid Common = Guid.NewGuid();
    private static readonly Guid Rare = Guid.NewGuid();
    private static readonly Guid Mythic = Guid.NewGuid();
    private static readonly Guid Unpriced = Guid.NewGuid();
    private static readonly Guid DeckCard = Guid.NewGuid();

    // Senza bulk (soglia 0) e senza costi, per verificare la sola aritmetica delle probabilità.
    private static readonly OpeningValueSettings NoBulk = new(0m, 0.05m, 0.35m, 0m);

    private static readonly Dictionary<(Guid, bool), decimal> Prices = new()
    {
        [(Common, false)] = 0.10m,
        [(Rare, false)] = 2.00m,
        [(Mythic, false)] = 10.00m,
        [(Rare, true)] = 6.00m,
        [(DeckCard, true)] = 20.00m
    };

    private static decimal? PriceOf(Guid uuid, bool foil) => Prices.TryGetValue((uuid, foil), out var p) ? p : null;

    /// <summary>
    /// Busta di prova: 10 comuni + 1 rara/mitica (rara 7 volte su 8, mitica 1 su 8). Una busta su
    /// quattro ha in più una foil, che è sempre la rara.
    /// </summary>
    private static OpeningValueCalculator Calculator(OpeningValueSettings settings, params MtgjsonDeck[] decks)
    {
        var configs = new Dictionary<string, List<BoosterConfig>>
        {
            ["TST:play"] = new()
            {
                new BoosterConfig { Weight = 3, TotalWeight = 4, Slots = { new() { SheetName = "common", Count = 10 }, new() { SheetName = "rareMythic", Count = 1 } } },
                new BoosterConfig { Weight = 1, TotalWeight = 4, Slots = { new() { SheetName = "common", Count = 10 }, new() { SheetName = "rareMythic", Count = 1 }, new() { SheetName = "foil", Count = 1 } } }
            }
        };

        var sheets = new Dictionary<(string, string), BoosterSheet>
        {
            [("TST:play", "common")] = new() { Name = "common", TotalWeight = 1, Cards = { new() { CardUuid = Common, Weight = 1 } } },
            [("TST:play", "rareMythic")] = new() { Name = "rareMythic", TotalWeight = 8, Cards = { new() { CardUuid = Rare, Weight = 7 }, new() { CardUuid = Mythic, Weight = 1 } } },
            [("TST:play", "foil")] = new() { Name = "foil", IsFoil = true, TotalWeight = 1, Cards = { new() { CardUuid = Rare, Weight = 1 } } }
        };

        return new OpeningValueCalculator(PriceOf, settings, configs, sheets,
            decks.ToDictionary(d => OpeningValueCalculator.DeckKey(d.SetCode, d.Name)));
    }

    [Fact]
    public void Pack_SumsProbabilityTimesPrice_AcrossConfigurationsAndSlots()
    {
        var pack = Calculator(NoBulk).Pack("TST:play")!;

        // 10 × 0,10 + (7/8 × 2 + 1/8 × 10) + 1/4 × 6 = 1 + 3 + 1,5
        pack.Value.Should().Be(5.5m);
        pack.PricedShare.Should().Be(1m);
        pack.TopCards.First().Uuid.Should().Be(Rare, "la rara contribuisce di più: 7/8 × 2 + 1/4 × 6");
    }

    [Fact]
    public void Bulk_IsWorthItsBulkPriceTimesTheShareThatSells()
    {
        var settings = new OpeningValueSettings(BulkThreshold: 0.25m, BulkPrice: 0.05m, BulkSellThrough: 0.40m, SellingCostPercent: 0m);

        var calculator = Calculator(settings);

        calculator.CardValue(Common, false).Should().Be(0.02m, "0,05 € × 40% venduto");
        calculator.CardValue(Rare, false).Should().Be(2.00m, "sopra soglia vale il suo prezzo");
        calculator.Pack("TST:play")!.Value.Should().Be(10 * 0.02m + 3m + 1.5m);
    }

    [Fact]
    public void UnpricedCards_LowerTheCoverage_InsteadOfCountingAsZeroSilently()
    {
        var configs = new Dictionary<string, List<BoosterConfig>>
        {
            ["TST:play"] = new() { new BoosterConfig { Weight = 1, TotalWeight = 1, Slots = { new() { SheetName = "mixed", Count = 2 } } } }
        };
        var sheets = new Dictionary<(string, string), BoosterSheet>
        {
            [("TST:play", "mixed")] = new() { Name = "mixed", TotalWeight = 4, Cards = { new() { CardUuid = Mythic, Weight = 1 }, new() { CardUuid = Unpriced, Weight = 3 } } }
        };
        var calculator = new OpeningValueCalculator(PriceOf, NoBulk, configs, sheets, new Dictionary<string, MtgjsonDeck>());

        var pack = calculator.Pack("TST:play")!;

        pack.Value.Should().Be(2 * 0.25m * 10m);
        pack.PricedShare.Should().Be(0.25m);
    }

    [Fact]
    public void Product_AddsPacksDecksAndFixedCards()
    {
        var deck = new MtgjsonDeck { SetCode = "TST", Name = "Scene", Cards = { new() { CardUuid = DeckCard, Count = 2, IsFoil = true } } };
        var calculator = Calculator(NoBulk, deck);

        var composition = new PackComposition();
        composition.AddPacks("TST:play", 3);
        composition.AddDeck(OpeningValueCalculator.DeckKey("TST", "Scene"), 1);
        composition.AddCard(Mythic, false, 1);

        var value = calculator.Product(composition);

        value.Gross.Should().Be(3 * 5.5m + 2 * 20m + 10m);
        value.Coverage.Should().Be(1m);
        value.MissingPacks.Should().BeEmpty();
    }

    [Fact]
    public void Product_WithoutBoosterData_ReportsTheMissingPack()
    {
        var composition = new PackComposition();
        composition.AddPacks("TRK:play", 30);

        var value = Calculator(NoBulk).Product(composition);

        value.Gross.Should().Be(0);
        value.Coverage.Should().Be(0);
        value.MissingPacks.Should().Equal("TRK:play");
    }

    [Fact]
    public void Net_SubtractsSellingCosts()
    {
        new OpeningValueSettings(0.25m, 0.05m, 0.35m, 15m).Net(100m).Should().Be(85m);
    }

    [Fact]
    public async Task BulkSellThrough_CountsOnlyExpansionsOpenedAtRelease()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var release = new DateTime(2026, 8, 14);
        var newSet = new Expansion { Id = 1, Name = "The Hobbit", Code = "hob", ReleaseDate = release };
        var oldSet = new Expansion { Id = 2, Name = "Khans of Tarkir", Code = "ktk", ReleaseDate = new DateTime(2014, 9, 26) };
        db.Expansions.AddRange(newSet, oldSet);
        db.Blueprints.AddRange(
            new Blueprint { Id = 10, ExpansionId = 1, Name = "Bulk nuova", Version = "" },
            new Blueprint { Id = 20, ExpansionId = 2, Name = "Bulk vecchia", Version = "" });
        db.InventoryItems.AddRange(
            new InventoryItem { BlueprintId = 10, Quantity = 600, ListingPrice = 0.05m, DateAdded = release.AddDays(2), Condition = "NM", Language = "English", Location = "" },
            new InventoryItem { BlueprintId = 20, Quantity = 900, ListingPrice = 0.05m, DateAdded = new DateTime(2025, 11, 21), Condition = "NM", Language = "English", Location = "" });
        var order = new Order { Id = 1, Code = "A" };
        db.Orders.Add(order);
        db.OrderItems.AddRange(
            new OrderItem { OrderId = 1, BlueprintId = 10, Quantity = 400, Price = 0.05m, Name = "x" },
            new OrderItem { OrderId = 1, BlueprintId = 20, Quantity = 100, Price = 0.05m, Name = "y" },
            new OrderItem { OrderId = 1, BlueprintId = 10, Quantity = 50, Price = 3m, Name = "rara" });
        await db.SaveChangesAsync();

        var result = await new BulkSellThroughService(db).MeasureAsync(0.25m);

        result.Measured.Should().BeTrue();
        result.Expansions.Should().ContainSingle(e => e.Name == "The Hobbit", "le espansioni vecchie si comprano come collezioni");
        result.Share.Should().Be(0.4m, "400 venduti su 1.000, la rara a 3 € non è bulk");
    }
}
