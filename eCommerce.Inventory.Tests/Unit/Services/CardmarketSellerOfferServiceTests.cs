using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>Offerte dei venditori Cardmarket incollate (feature 007, Fase 1). Venditore e prezzi inventati.</summary>
public class CardmarketSellerOfferServiceTests
{
    private static string Page(params (string Name, string Comment, string Price, int Quantity)[] offers) =>
        "Home\nUser search\nVenditoreProva\nOffers\nPreconstructed Decks\nEnglish (3)\nName\nInfo.\nOffer\n" +
        string.Concat(offers.Select(o => $"{o.Name}\n{o.Comment}\n{o.Price}\n{o.Quantity}\n")) + "3 Hits\n";

    private static async Task<ApplicationDbContext> DatabaseAsync()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        // Nel listino importato le virgolette sono raddoppiate.
        db.CardmarketProducts.AddRange(
            new CardmarketProduct { IdProduct = 100, Name = "Commander: Aetherdrift: \"\"Living Energy\"\" Commander Deck", CategoryName = "Magic Intropack" },
            new CardmarketProduct { IdProduct = 200, Name = "Commander: Bloomburrow: \"\"Family Matters\"\" Commander Deck", CategoryName = "Magic Intropack" });
        db.SealedProducts.AddRange(
            new SealedProduct { Id = 1, Name = "Aetherdrift Commander Deck Living Energy", SetCode = "DFT", CardmarketId = 100 },
            new SealedProduct { Id = 2, Name = "Bloomburrow Commander Deck Family Matters", SetCode = "BLB", CardmarketId = 200 });
        db.SealedOpportunities.AddRange(
            new SealedOpportunity { SealedProductId = 1, Date = new DateOnly(2026, 10, 17), MainSetCode = "DFT", Decision = "Apri", OpenValueCm = 50m, CmTrend = 37m },
            new SealedOpportunity { SealedProductId = 2, Date = new DateOnly(2026, 10, 17), MainSetCode = "BLB", Decision = "Tieni sigillato", OpenValueCm = 42m, CmTrend = 43m, HoldScore = 87m, IsHoldCandidate = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static CardmarketSellerOfferService Service(ApplicationDbContext db) =>
        new(db, new ConfigurationBuilder().Build(), NullLogger<CardmarketSellerOfferService>.Instance);

    [Fact]
    public async Task Import_matches_the_catalog_and_evaluates_each_offer()
    {
        await using var db = await DatabaseAsync();

        var result = await Service(db).ImportAsync(null, Page(
            ("Commander: Aetherdrift: \"Living Energy\" Commander Deck", "free shipping", "33,00 €", 11),
            ("Commander: Bloomburrow: \"Family Matters\" Commander Deck", "", "40,00 €", 2),
            ("Something not in the catalog", "", "9,00 €", 1)), CancellationToken.None);

        result.Seller.Should().Be("VenditoreProva");
        result.EnglishFilter.Should().BeTrue();
        result.Unmatched.Should().Equal("Something not in the catalog");
        result.Offers.Select(o => o.Verdict).Should().Equal("Da aprire: guadagno 17,00 €", "Da tenere: punteggio 87, sotto il trend");
        (await Service(db).GetValidOffersAsync(CancellationToken.None)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_new_paste_replaces_the_offers_of_its_products_and_keeps_the_others()
    {
        await using var db = await DatabaseAsync();
        var service = Service(db);
        await service.ImportAsync(null, Page(
            ("Commander: Aetherdrift: \"Living Energy\" Commander Deck", "", "33,00 €", 11),
            ("Commander: Bloomburrow: \"Family Matters\" Commander Deck", "", "40,00 €", 2)), CancellationToken.None);

        await service.ImportAsync(null, Page(("Commander: Aetherdrift: \"Living Energy\" Commander Deck", "DAMAGED!", "30,00 €", 1)), CancellationToken.None);

        var offers = await db.CardmarketSellerOffers.ToListAsync();
        offers.Should().HaveCount(2);
        offers.Single(o => o.IdProduct == 100).Should().Match<CardmarketSellerOffer>(o => o.Price == 30m && o.IsDamaged);
        // Le danneggiate restano visibili ma non vanno al pianificatore.
        (await service.GetValidOffersAsync(CancellationToken.None)).Should().ContainSingle().Which.SealedProductId.Should().Be(2);
    }

    [Fact]
    public async Task Page_one_replaces_the_whole_category_so_sold_products_disappear()
    {
        await using var db = await DatabaseAsync();
        var service = Service(db);
        await service.ImportAsync(null, Page(
            ("Commander: Aetherdrift: \"Living Energy\" Commander Deck", "", "33,00 €", 11),
            ("Commander: Bloomburrow: \"Family Matters\" Commander Deck", "", "40,00 €", 2)) + "Page 1 of 1\n", CancellationToken.None);

        // Il venditore ha venduto il mazzo Bloomburrow: la pagina 1 reincollata non lo contiene più.
        await service.ImportAsync(null, Page(("Commander: Aetherdrift: \"Living Energy\" Commander Deck", "", "31,00 €", 10)) + "Page 1 of 1\n",
            CancellationToken.None);

        (await db.CardmarketSellerOffers.ToListAsync()).Should().ContainSingle().Which.Price.Should().Be(31m);
    }

    [Fact]
    public async Task The_seller_written_in_the_page_wins_over_the_field()
    {
        await using var db = await DatabaseAsync();

        var result = await Service(db).ImportAsync("VenditoreDiPrima",
            Page(("Commander: Aetherdrift: \"Living Energy\" Commander Deck", "", "33,00 €", 11)), CancellationToken.None);

        result.Seller.Should().Be("VenditoreProva");
        (await db.CardmarketSellerOffers.SingleAsync()).Seller.Should().Be("VenditoreProva");
    }
}
