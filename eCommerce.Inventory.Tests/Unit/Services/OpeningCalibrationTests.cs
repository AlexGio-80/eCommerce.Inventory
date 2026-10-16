using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Taratura del modello sulle vendite reali (Fase 5): fattore prezzo realizzato, bilancio per
/// apertura e registro acquisti.
/// </summary>
public class OpeningCalibrationTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private void SeedCard(int blueprintId, int cmId, decimal trend, string? tag = null)
    {
        if (!_db.Expansions.Local.Any(e => e.Id == 1))
            _db.Expansions.Add(new Expansion { Id = 1, Name = "Lorwyn Eclipsed", Code = "ecl" });
        _db.Blueprints.Add(new Blueprint { Id = blueprintId, ExpansionId = 1, Name = $"Carta {blueprintId}", Version = "", CardMarketIds = $"[{cmId}]" });
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = cmId, Trend = trend });
    }

    private void Sell(int orderId, int blueprintId, int quantity, decimal price, DateTime paidAt, string? tag = null)
    {
        if (!_db.Orders.Local.Any(o => o.Id == orderId))
            _db.Orders.Add(new Order { Id = orderId, Code = $"O{orderId}", PaidAt = paidAt, SellerSubtotal = 100m, SellerFee = 5m });
        _db.OrderItems.Add(new OrderItem { OrderId = orderId, BlueprintId = blueprintId, Quantity = quantity, Price = price, Name = "x", Tag = tag });
    }

    [Theory]
    [InlineData("[123456]", 123456)]
    [InlineData("[7, 8]", 7)]
    [InlineData("[]", null)]
    [InlineData("non json", null)]
    [InlineData(null, null)]
    public void FirstCardmarketId_ReadsTheFirstIdOfTheJsonArray(string? json, int? expected) =>
        PriceRealizationService.FirstCardmarketId(json).Should().Be(expected);

    [Fact]
    public async Task PriceRealization_ComparesRecentSalesWithTrend_IgnoringCheapCardsAndOldSales()
    {
        SeedCard(1, 101, trend: 10m);
        SeedCard(2, 102, trend: 0.20m);
        Sell(1, 1, 10, 12m, DateTime.UtcNow.AddDays(-5));       // 120 incassati su 100 di trend
        Sell(1, 2, 100, 0.30m, DateTime.UtcNow.AddDays(-5));    // sotto 1 € di trend: esclusa
        Sell(2, 1, 10, 30m, DateTime.UtcNow.AddDays(-60));      // vecchia: esclusa
        await _db.SaveChangesAsync();

        var result = await new PriceRealizationService(_db).MeasureAsync();

        result.Measured.Should().BeTrue();
        result.Factor.Should().Be(1.2m);
        result.Copies.Should().Be(10);
    }

    [Fact]
    public async Task PriceRealization_WithTooFewSales_FallsBackToOne()
    {
        SeedCard(1, 101, trend: 5m);
        Sell(1, 1, 1, 10m, DateTime.UtcNow.AddDays(-1));
        await _db.SaveChangesAsync();

        var result = await new PriceRealizationService(_db).MeasureAsync();

        result.Measured.Should().BeFalse();
        result.Factor.Should().Be(1m);
    }

    /// <summary>Carta di un set MTGJSON del tipo dato, abbinata alla stampa tramite Scryfall.</summary>
    private void SeedTypedCard(int blueprintId, int cmId, decimal trend, string setCode, string setType)
    {
        SeedCard(blueprintId, cmId, trend);
        var scryfallId = $"scry-{blueprintId}";
        _db.Blueprints.Local.Single(b => b.Id == blueprintId).ScryfallId = scryfallId;
        if (!_db.MtgjsonSets.Local.Any(s => s.Code == setCode))
            _db.MtgjsonSets.Add(new MtgjsonSet { Code = setCode, Name = setCode, Type = setType });
        _db.MtgjsonCards.Add(new MtgjsonCard { Uuid = Guid.NewGuid(), SetCode = setCode, Name = $"Carta {blueprintId}", ScryfallId = scryfallId });
    }

    [Fact]
    public async Task PriceRealization_IsMeasuredByCardKind_FallingBackToTheOverallFactor()
    {
        SeedTypedCard(1, 101, trend: 10m, "FRA", "expansion");
        SeedTypedCard(2, 102, trend: 10m, "FRC", "commander");
        SeedTypedCard(3, 103, trend: 10m, "SLD", "box");
        Sell(1, 1, 10, 15m, DateTime.UtcNow.AddDays(-5));   // buste: 150 su 100
        Sell(1, 2, 10, 9m, DateTime.UtcNow.AddDays(-5));    // mazzi: 90 su 100
        Sell(1, 3, 2, 20m, DateTime.UtcNow.AddDays(-5));    // Secret Lair: 20 di trend, campione piccolo
        await _db.SaveChangesAsync();

        var result = await new PriceRealizationService(_db).MeasureAsync();

        result.Factor.Should().Be(1.273m, "280 incassati su 220 di trend");
        result.FactorFor(CardKind.Booster).Should().Be(1.5m);
        result.FactorFor(CardKind.Deck).Should().Be(0.9m);
        result.Kinds.Single(k => k.Kind == CardKind.SecretLair).Measured.Should().BeFalse();
        result.FactorFor(CardKind.SecretLair).Should().Be(1.273m, "con un campione piccolo vale il complessivo");
    }

    [Fact]
    public async Task PriceRealization_CountsCtZeroSalesNotYetPaid_OnceOnly_AndIgnoresFreeRows()
    {
        SeedCard(1, 101, trend: 10m);
        // Ordine pagato nella finestra: fissa il numero d'ordine Card Trader da cui partire.
        _db.Orders.Add(new Order { Id = 1, CardTraderOrderId = 1000, Code = "PAID", State = "done", PaidAt = DateTime.UtcNow.AddDays(-5) });
        _db.Orders.Add(new Order { Id = 2, CardTraderOrderId = 1001, Code = "HUB-ABBINATO", State = "hub_pending" });
        _db.Orders.Add(new Order { Id = 3, CardTraderOrderId = 1002, Code = "HUB-NUOVO", State = "hub_pending" });
        _db.Orders.Add(new Order { Id = 4, CardTraderOrderId = 500, Code = "HUB-VECCHIO", State = "hub_pending" });
        _db.OrderItems.AddRange(
            new OrderItem { Id = 20, OrderId = 2, BlueprintId = 1, Quantity = 5, Price = 12m, Name = "x" },
            new OrderItem { Id = 10, OrderId = 1, BlueprintId = 1, Quantity = 5, Price = 12m, Name = "x", HubOrderItemId = 20 },
            new OrderItem { Id = 30, OrderId = 3, BlueprintId = 1, Quantity = 5, Price = 8m, Name = "x" },
            new OrderItem { Id = 31, OrderId = 3, BlueprintId = 1, Quantity = 3, Price = 0m, Name = "x" },
            new OrderItem { Id = 40, OrderId = 4, BlueprintId = 1, Quantity = 5, Price = 30m, Name = "x" });
        await _db.SaveChangesAsync();

        var result = await new PriceRealizationService(_db).MeasureAsync();

        result.Copies.Should().Be(10, "la riga pagata e la hub_pending non ancora pagata; non la gemella, né la riga a 0, né l'ordine fuori finestra");
        result.Factor.Should().Be(1m, "100 incassati su 100 di trend");
    }

    [Fact]
    public void PriceFactor_FollowsTheKindOfEachCard()
    {
        var booster = Guid.NewGuid();
        var deck = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var settings = new OpeningValueSettings(0.25m, 0.05m, 0.40m, 0m, PriceFactor: 1.2m,
            PriceFactors: new Dictionary<CardKind, decimal> { [CardKind.Booster] = 1.5m, [CardKind.Deck] = 0.9m });
        var calculator = new OpeningValueCalculator(
            (_, _) => 10m, settings,
            new Dictionary<string, List<BoosterConfig>>(), new Dictionary<(string, string), BoosterSheet>(),
            new Dictionary<string, MtgjsonDeck>(),
            uuid => uuid == booster ? CardKind.Booster : uuid == deck ? CardKind.Deck : null);

        calculator.CardValue(booster, false).Should().Be(15m);
        calculator.CardValue(deck, false).Should().Be(9m);
        calculator.CardValue(unknown, false).Should().Be(12m, "senza tipo vale il fattore complessivo");
    }

    [Theory]
    [InlineData("FRA", "expansion", CardKind.Booster)]
    [InlineData("PIP", "commander", CardKind.Deck)]
    [InlineData("DDU", "duel_deck", CardKind.Deck)]
    [InlineData("SLD", "box", CardKind.SecretLair)]
    [InlineData("SLU", "box", CardKind.SecretLair)]
    [InlineData("XYZ", null, CardKind.Booster)]
    public void CardKind_ComesFromTheSetOfThePrinting(string setCode, string? setType, CardKind expected) =>
        CardKinds.Of(setCode, setType).Should().Be(expected);

    [Fact]
    public void PriceFactor_RaisesCardsAboveTheBulkThreshold_ButNotTheBulk()
    {
        var expensive = Guid.NewGuid();
        var bulk = Guid.NewGuid();
        var settings = new OpeningValueSettings(0.25m, 0.05m, 0.40m, 0m, PriceFactor: 1.2m);
        var calculator = new OpeningValueCalculator(
            (uuid, _) => uuid == expensive ? 10m : 0.10m, settings,
            new Dictionary<string, List<BoosterConfig>>(), new Dictionary<(string, string), BoosterSheet>(),
            new Dictionary<string, MtgjsonDeck>());

        calculator.CardValue(expensive, false).Should().Be(12m);
        calculator.CardValue(bulk, false).Should().Be(0.02m);
    }

    [Fact]
    public async Task OpeningBalance_SumsCostSalesAndStockByTag_FromTheFirstUpload()
    {
        SeedCard(1, 101, trend: 5m);
        var opened = new DateTime(2026, 2, 5);
        _db.PendingListings.AddRange(
            new PendingListing { BlueprintId = 1, Quantity = 30, PurchasePrice = 2m, SellingPrice = 9m, Tag = "#ECL_20260205", CreatedAt = opened, Condition = "NM", Language = "English" },
            new PendingListing { BlueprintId = 1, Quantity = 10, PurchasePrice = 2m, SellingPrice = 9m, Tag = "#ECL_20260205", CreatedAt = opened.AddDays(3), Condition = "NM", Language = "English" });
        Sell(1, 1, 4, 5m, opened.AddDays(10), "#ECL_20260205");
        Sell(2, 1, 2, 5m, opened.AddDays(100), "ecl_20260205");     // stesso tag scritto diversamente
        Sell(3, 1, 7, 5m, new DateTime(2014, 5, 13), "#ECL_20260205"); // tag attribuito per errore a un ordine storico
        _db.InventoryItems.Add(new InventoryItem { BlueprintId = 1, Quantity = 20, ListingPrice = 0.05m, Tag = "#ECL_20260205", Condition = "NM", Language = "English", Location = "" });
        await _db.SaveChangesAsync();

        var balance = (await new OpeningBalanceService(_db, new PurchaseCostService(_db)).GetAsync(0.25m)).Single();

        balance.Copies.Should().Be(40);
        balance.Cost.Should().Be(80m);
        balance.SoldCopies.Should().Be(6, "la vendita del 2014 è precedente all'apertura");
        balance.GrossRevenue.Should().Be(30m);
        balance.NetRevenue.Should().Be(28.5m, "commissione misurata sugli ordini: 5%");
        balance.StockCopies.Should().Be(20);
        balance.StockBulkCopies.Should().Be(20);
        balance.ProfitSoFar.Should().Be(-51.5m);
        balance.RevenueCurve.First(p => p.Days == 30).NetRevenue.Should().Be(19m);
        balance.RevenueCurve.First(p => p.Days == 180).NetRevenue.Should().Be(28.5m);
        balance.OpenedAtRelease.Should().BeFalse("l'espansione di prova non ha data di uscita");
    }

    [Fact]
    public async Task OpeningBalance_CountsOnlyTheCopiesAddedByAnUpdate()
    {
        SeedCard(1, 101, trend: 0.05m);
        var opened = new DateTime(2026, 6, 26);
        _db.PendingListings.AddRange(
            new PendingListing { BlueprintId = 1, Quantity = 100, PurchasePrice = 1m, SellingPrice = 0.05m, Tag = "#MSH_OLD", CreatedAt = opened, Condition = "NM", Language = "English", CardTraderProductId = 555 },
            // Modifica: la quantità è il nuovo totale dell'inserzione, non le copie aggiunte
            new PendingListing { BlueprintId = 1, Quantity = 130, PurchasePrice = 1m, SellingPrice = 0.05m, Tag = "#MSH_OLD", CreatedAt = opened.AddDays(20), Condition = "NM", Language = "English", CardTraderProductId = 555, IsUpdate = true },
            // Seconda modifica dopo 10 vendite: lo storico prezzi dice che prima ce n'erano 120
            new PendingListing { BlueprintId = 1, Quantity = 150, PurchasePrice = 1m, SellingPrice = 0.05m, Tag = "#MSH_OLD", CreatedAt = opened.AddDays(40), Condition = "NM", Language = "English", CardTraderProductId = 555, IsUpdate = true });
        _db.PriceHistoryEntries.Add(new PriceHistoryEntry { BlueprintId = 1, CardTraderProductId = 555, Quantity = 120, Price = 0.05m, RecordedAt = opened.AddDays(39) });
        await _db.SaveChangesAsync();

        var balance = (await new OpeningBalanceService(_db, new PurchaseCostService(_db)).GetAsync(0.25m)).Single();

        balance.Copies.Should().Be(160, "100 iniziali + 30 + 30, non 100 + 130 + 150");
        balance.Cost.Should().Be(160m);
    }

    [Fact]
    public async Task Purchase_IsValidated_AndLeavesThePredictionEmptyWhenDataAreMissing()
    {
        _db.MtgjsonSets.Add(new MtgjsonSet { Code = "TRK", Name = "Star Trek" });
        var box = new SealedProduct
        {
            Uuid = Guid.NewGuid(), SetCode = "TRK", Name = "Star Trek Play Booster Box", Category = "booster_box",
            Contents = { new SealedProductContent { Kind = SealedContentKind.Pack, PackCode = "play", SetCode = "TRK" } }
        };
        _db.SealedProducts.Add(box);
        await _db.SaveChangesAsync();

        var analysis = new SealedProductAnalysisService(_db, Mock.Of<ICardTraderApiService>(), new BulkSellThroughService(_db),
            new PriceRealizationService(_db), new ConfigurationBuilder().Build(), NullLogger<SealedProductAnalysisService>.Instance);
        var service = new ProductPurchaseService(_db, analysis);

        var invalid = () => service.SaveAsync(null, new ProductPurchaseInput(box.Id, 0, 150.00m, null, null, null, null, null, null));
        await invalid.Should().ThrowAsync<ArgumentException>();

        var saved = await service.SaveAsync(null, new ProductPurchaseInput(
            box.Id, 6, 150.00m, "Cardmarket", "Venditore di prova", new DateOnly(2026, 9, 20), null, "#TRK_PB_20261113", null));

        saved.TotalPrice.Should().Be(900.00m);
        saved.PredictedOpenValueNet.Should().BeNull("senza la composizione delle buste la previsione sarebbe sottostimata");
        (await service.ListAsync()).Should().ContainSingle(p => p.ProductName == "Star Trek Play Booster Box");
    }

    private ProductPurchaseService PurchaseService() => new(_db, new SealedProductAnalysisService(_db, Mock.Of<ICardTraderApiService>(),
        new BulkSellThroughService(_db), new PriceRealizationService(_db), new ConfigurationBuilder().Build(),
        NullLogger<SealedProductAnalysisService>.Instance));

    /// <summary>Box da 2 buste; ogni busta ha 14 o 15 carte, metà e metà.</summary>
    private SealedProduct SeedBox(string setCode, bool withBoosterData)
    {
        var pack = new SealedProduct
        {
            Uuid = Guid.NewGuid(), SetCode = setCode, Name = "Play Booster Pack", Category = "booster_pack",
            Contents = { new SealedProductContent { Kind = SealedContentKind.Pack, PackCode = "play", SetCode = setCode } }
        };
        var box = new SealedProduct
        {
            Uuid = Guid.NewGuid(), SetCode = setCode, Name = "Play Booster Box", Category = "booster_box",
            Contents = { new SealedProductContent { Kind = SealedContentKind.Sealed, ChildUuid = pack.Uuid, Count = 2 } }
        };
        _db.SealedProducts.AddRange(pack, box);
        if (withBoosterData)
        {
            _db.BoosterConfigs.AddRange(
                new BoosterConfig { SetCode = setCode, BoosterType = "play", Weight = 1, TotalWeight = 2, Slots = { new BoosterConfigSlot { SheetName = "common", Count = 14 } } },
                new BoosterConfig { SetCode = setCode, BoosterType = "play", Weight = 1, TotalWeight = 2, Slots = { new BoosterConfigSlot { SheetName = "common", Count = 15 } } });
        }
        return box;
    }

    [Fact]
    public async Task CostPerCard_IsThePriceDividedByTheCardsInTheProduct_UnlessWrittenByHand()
    {
        var box = SeedBox("AAA", withBoosterData: true);
        await _db.SaveChangesAsync();
        var service = PurchaseService();

        var calculated = await service.SaveAsync(null, new ProductPurchaseInput(box.Id, 1, 29.00m, null, null, null, null, "#AAA_PB", null));

        calculated.CardsPerUnit.Should().Be(29, "2 buste da 14,5 carte in media");
        calculated.CalculatedCostPerCard.Should().Be(1.00m);
        calculated.EffectiveCostPerCard.Should().Be(1.00m);
        calculated.CostPerCard.Should().BeNull();

        var byHand = await service.SaveAsync(calculated.Id, new ProductPurchaseInput(box.Id, 1, 29.00m, null, null, null, null, "#AAA_PB", null, 0.90m));

        byHand.CostPerCard.Should().Be(0.90m);
        byHand.CalculatedCostPerCard.Should().Be(1.00m, "il calcolato resta visibile accanto");
        byHand.EffectiveCostPerCard.Should().Be(0.90m);
    }

    [Fact]
    public async Task CatalogSearch_FindsProductsByCardmarketName_MtgjsonName_OrSet()
    {
        _db.MtgjsonSets.Add(new MtgjsonSet { Code = "FDC", Name = "Foundations Commander", ReleaseDate = new DateOnly(2024, 11, 15) });
        _db.CardmarketProducts.Add(new CardmarketProduct { IdProduct = 903009, Name = "Commander: Foundations: Deck Set" });
        _db.SealedProducts.AddRange(
            new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "FDC", Name = "Foundations Commander Decks Set of 5", Category = "subset", CardmarketId = 903009 },
            new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "FDC", Name = "Foundations Commander Deck Wretched Ranks", Category = "deck" });
        await _db.SaveChangesAsync();
        var service = PurchaseService();

        var byCardmarket = await service.SearchCatalogAsync("foundations deck set");
        byCardmarket.Should().ContainSingle().Which.Should().Match<CatalogProductDto>(p =>
            p.Name == "Foundations Commander Decks Set of 5" && p.CardmarketName == "Commander: Foundations: Deck Set");

        (await service.SearchCatalogAsync("wretched")).Should().ContainSingle(p => p.CardmarketName == null);
        (await service.SearchCatalogAsync("FDC")).Should().HaveCount(2, "il codice del set trova tutti i suoi prodotti");
        (await service.SearchCatalogAsync("  ")).Should().BeEmpty();
    }

    [Fact]
    public async Task CostPerCard_UsesTypicalPacksWhenTheCompositionIsNotPublished()
    {
        var box = SeedBox("BBB", withBoosterData: false);
        await _db.SaveChangesAsync();

        var saved = await PurchaseService().SaveAsync(null, new ProductPurchaseInput(box.Id, 1, 28.00m, null, null, null, null, null, null));

        saved.CardsPerUnit.Should().Be(28, "una Play Booster ha di solito 14 carte");
        saved.CardsEstimated.Should().BeTrue();
        saved.CalculatedCostPerCard.Should().Be(1.00m);
    }
}
