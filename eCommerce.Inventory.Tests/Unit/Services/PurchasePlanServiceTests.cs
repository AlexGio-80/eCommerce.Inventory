using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Piano d'acquisto su Card Trader: offerte convenienti raggruppate per venditore, per comprare più
/// prodotti con un'unica spedizione, e carrello Card Trader Zero (venditori diversi, una spedizione).
/// </summary>
public class PurchasePlanServiceTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Mock<ICardTraderApiService> _cardTrader = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private async Task<SealedProduct> AddProductAsync(string name, int blueprintId, decimal openValueNet)
    {
        var product = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "MSH", Name = name, Category = "booster_box", CardTraderBlueprintId = blueprintId };
        _db.SealedProducts.Add(product);
        await _db.SaveChangesAsync();
        _db.SealedOpportunities.Add(new SealedOpportunity { Date = Today, SealedProductId = product.Id, MainSetCode = "MSH", OpenValueCm = openValueNet, CmTrend = openValueNet / 1.5m, Decision = "Apri" });
        await _db.SaveChangesAsync();
        return product;
    }

    private static CardTraderMarketplaceProductDto Offer(int blueprintId, int sellerId, string seller, decimal price, bool ctZero = false, string language = "en", int quantity = 2) => new()
    {
        BlueprintId = blueprintId,
        PriceCents = (int)(price * 100),
        Quantity = quantity,
        PropertiesHash = new Dictionary<string, object> { ["mtg_language"] = language },
        User = new CardTraderMarketplaceUserDto { Id = sellerId, Username = seller, CountryCode = "IT", CanSellSealedWithCtZero = ctZero }
    };

    private void SetupOffers(int blueprintId, params CardTraderMarketplaceProductDto[] offers) =>
        _cardTrader.Setup(c => c.GetMarketplaceProductsAsync(blueprintId, It.IsAny<CancellationToken>())).ReturnsAsync(offers);

    private PurchasePlanService CreateService() => new(_db, _cardTrader.Object, NullLogger<PurchasePlanService>.Instance);

    [Fact]
    public async Task Plan_GroupsConvenientOffersBySeller_AndBuildsTheCtZeroBasket()
    {
        var marvel = await AddProductAsync("Marvel Super Heroes Jumpstart Booster Box", 100, openValueNet: 190m);
        var mom = await AddProductAsync("March of the Machine Jumpstart Booster Box", 200, openValueNet: 130m);

        SetupOffers(100,
            Offer(100, 1, "BoxShop", 110m),
            Offer(100, 1, "BoxShop", 115m),                       // stessa merce più cara: si tiene la più economica
            Offer(100, 2, "ZeroSeller", 105m, ctZero: true),
            Offer(100, 3, "Caro", 250m),                          // sopra il valore atteso: non conviene
            Offer(100, 4, "Tedesco", 90m, language: "de"));       // non inglese: escluso
        SetupOffers(200,
            Offer(200, 1, "BoxShop", 80m),
            Offer(200, 5, "AltroZero", 78m, ctZero: true));

        var plan = await CreateService().BuildAsync(new[] { marvel.Id, mom.Id });

        var first = plan.Sellers.First();
        first.SellerName.Should().Be("BoxShop", "è l'unico ad avere entrambi i box");
        first.ProductCount.Should().Be(2);
        first.Total.Should().Be(190m);
        first.Margin.Should().Be(130m, "320 € di valore atteso netto contro 190 € di spesa");
        plan.Sellers.Should().NotContain(s => s.SellerName == "Caro" || s.SellerName == "Tedesco");

        plan.CtZero.ProductCount.Should().Be(2, "con CT Zero due venditori diversi arrivano in un'unica spedizione");
        plan.CtZero.Total.Should().Be(183m);
        plan.Products.Single(p => p.ProductId == marvel.Id).CheapestPrice.Should().Be(105m);
    }

    [Fact]
    public async Task Plan_RejectsTooManyProducts()
    {
        var build = () => CreateService().BuildAsync(Enumerable.Range(1, PurchasePlanService.MaxProducts + 1).ToList());

        await build.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Plan_ReportsProductsWithoutCardTraderBlueprint_WithoutCallingCardTrader()
    {
        var product = new SealedProduct { Uuid = Guid.NewGuid(), SetCode = "MSH", Name = "Senza blueprint", Category = "booster_box" };
        _db.SealedProducts.Add(product);
        await _db.SaveChangesAsync();

        var plan = await CreateService().BuildAsync(new[] { product.Id });

        plan.Products.Single().Note.Should().Contain("blueprint");
        _cardTrader.Verify(c => c.GetMarketplaceProductsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
