using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Dalla lettura della foto alle carte del database (feature 006). La corrispondenza è nostra: una
/// lettura sbagliata deve dare candidati da scartare, mai una carta inventata o la stampa sbagliata
/// in cima.
/// </summary>
public class CardScanServiceTests
{
    private static readonly Expansion DoubleMasters = new() { Id = 1, Name = "Double Masters 2022", Code = "2x2", ReleaseDate = new DateTime(2022, 7, 8) };
    private static readonly Expansion Magic2010 = new() { Id = 2, Name = "Magic 2010", Code = "m10", ReleaseDate = new DateTime(2009, 7, 17) };

    private static Blueprint Card(int id, string name, Expansion expansion, string number, string? italian = null) => new()
    {
        Id = id,
        CardTraderId = 1000 + id,
        Name = name,
        ItalianName = italian,
        Version = string.Empty,
        Expansion = expansion,
        ExpansionId = expansion.Id,
        FixedProperties = $"{{\"mtg_rarity\":\"Uncommon\",\"collector_number\":\"{number}\"}}"
    };

    [Theory]
    [InlineData("054", "54")]
    [InlineData("117/332", "117")]
    [InlineData("0", "0")]
    [InlineData("12a", "12a")]
    [InlineData(" 7 ", "7")]
    public void NormalizeNumber_ignores_leading_zeros_and_the_total(string printed, string expected)
    {
        CardScanService.NormalizeNumber(printed).Should().Be(expected);
    }

    [Fact]
    public void Rank_puts_the_exact_print_first()
    {
        var pool = new List<Blueprint>
        {
            Card(1, "Lightning Bolt", Magic2010, "146"),
            Card(2, "Lightning Bolt", DoubleMasters, "366"),
            Card(3, "Lightning Bolt", DoubleMasters, "117"),
        };
        var reading = new CardReading { Name = "Lightning Bolt", SetCode = "2x2", CollectorNumber = "117" };

        var ranked = CardScanService.Rank(reading, pool);

        ranked.Select(c => c.BlueprintId).Should().Equal(3, 2, 1);
        ranked[0].ExactPrint.Should().BeTrue();
        ranked.Skip(1).Should().OnlyContain(c => !c.ExactPrint);
    }

    [Fact]
    public void Rank_finds_the_borderless_print_in_the_collectors_expansion()
    {
        // Sulla carta c'è "AFR 290", ma Card Trader la tiene in "cafr" (Collectors).
        var afr = new Expansion { Id = 10, Name = "D&D: Adventures in the Forgotten Realms", Code = "afr" };
        var cafr = new Expansion { Id = 11, Name = "D&D: Adventures in the Forgotten Realms Collectors", Code = "cafr" };
        var pafr = new Expansion { Id = 12, Name = "D&D: Adventures in the Forgotten Realms Promos", Code = "pafr" };
        var pool = new List<Blueprint>
        {
            Card(1, "Iymrith, Desert Doom", afr, "062"),
            Card(2, "Iymrith, Desert Doom", cafr, "290"),
            Card(3, "Iymrith, Desert Doom", pafr, "062"),
        };

        var borderless = CardScanService.Rank(
            new CardReading { Name = "Iymrith, Desert Doom", SetCode = "afr", CollectorNumber = "290" }, pool);
        borderless[0].BlueprintId.Should().Be(2);
        borderless[0].ExactPrint.Should().BeTrue();

        // Numero del set base: prima la carta del set base, non la promo con lo stesso numero.
        var regular = CardScanService.Rank(
            new CardReading { Name = "Iymrith, Desert Doom", SetCode = "afr", CollectorNumber = "62" }, pool);
        regular[0].BlueprintId.Should().Be(1);
    }

    [Fact]
    public void Rank_drops_cards_of_the_set_that_match_neither_name_nor_number()
    {
        var pool = new List<Blueprint>
        {
            Card(1, "Lightning Bolt", DoubleMasters, "117"),
            Card(2, "Counterspell", DoubleMasters, "45"),
        };
        var reading = new CardReading { Name = "Lightning Bolt", SetCode = "2x2", CollectorNumber = "117" };

        CardScanService.Rank(reading, pool).Select(c => c.BlueprintId).Should().Equal(1);
    }

    [Fact]
    public void Rank_finds_the_print_by_set_and_number_even_if_the_name_is_misread()
    {
        var pool = new List<Blueprint> { Card(1, "Lightning Bolt", DoubleMasters, "117") };
        var reading = new CardReading { Name = "Lightnimg Bolf", SetCode = "2x2", CollectorNumber = "117" };

        var ranked = CardScanService.Rank(reading, pool);

        ranked.Should().ContainSingle().Which.ExactPrint.Should().BeTrue();
    }

    [Fact]
    public void Rank_matches_the_italian_printed_name()
    {
        var pool = new List<Blueprint> { Card(1, "Lightning Bolt", Magic2010, "146", italian: "Fulmine") };
        var reading = new CardReading { PrintedName = "Fulmine", Language = "Italian" };

        CardScanService.Rank(reading, pool).Should().ContainSingle().Which.BlueprintId.Should().Be(1);
    }

    [Fact]
    public void Rank_without_set_prefers_the_most_recent_release()
    {
        var pool = new List<Blueprint>
        {
            Card(1, "Lightning Bolt", Magic2010, "146"),
            Card(2, "Lightning Bolt", DoubleMasters, "117"),
        };

        CardScanService.Rank(new CardReading { Name = "Lightning Bolt" }, pool)
            .Select(c => c.BlueprintId).Should().Equal(2, 1);
    }

    [Fact]
    public async Task FindCandidates_only_looks_at_magic_cards()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var magic = new Game { Id = 1, CardTraderId = 1, Name = "Magic", Code = "mtg" };
        var other = new Game { Id = 2, CardTraderId = 5, Name = "Pokémon", Code = "pkm" };
        var mtgSet = new Expansion { Id = 1, Name = "Double Masters 2022", Code = "2x2", Game = magic };
        var otherSet = new Expansion { Id = 2, Name = "Other 2X2", Code = "2x2", Game = other };
        db.AddRange(magic, other, mtgSet, otherSet);
        db.Blueprints.AddRange(
            new Blueprint { Id = 1, CardTraderId = 11, Name = "Lightning Bolt", Version = "", Expansion = mtgSet, Game = magic, FixedProperties = "{\"collector_number\":\"117\"}" },
            new Blueprint { Id = 2, CardTraderId = 12, Name = "Lightning Bolt", Version = "", Expansion = otherSet, Game = other, FixedProperties = "{\"collector_number\":\"117\"}" });
        await db.SaveChangesAsync();

        var service = new CardScanService(db, null!, new ScanDailyCounter(), null!, null!, null!,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CardScanService>.Instance);

        var candidates = await service.FindCandidatesAsync(
            new CardReading { Name = "Lightning Bolt", SetCode = "2x2", CollectorNumber = "117" }, CancellationToken.None);

        candidates.Should().ContainSingle().Which.BlueprintId.Should().Be(1);
    }

    [Fact]
    public void SameKind_keeps_only_comparable_offers_of_other_sellers()
    {
        static CardTraderMarketplaceProductDto Offer(int cents, string condition = "Near Mint", string language = "en",
            bool foil = false, int seller = 2, bool ctZero = false, int quantity = 1) => new()
        {
            PriceCents = cents,
            Quantity = quantity,
            Properties = new CardTraderMarketplacePropertiesDto { Condition = condition, Language = language, IsFoil = foil },
            User = new CardTraderMarketplaceUserDto { Id = seller, CanSellViaHub = ctZero }
        };

        var offers = new[]
        {
            Offer(395),
            Offer(410, ctZero: true),
            Offer(300, seller: 99),            // mia
            Offer(250, condition: "Played"),
            Offer(200, language: "it"),
            Offer(150, foil: true),
            Offer(100, quantity: 0),
            Offer(200_000),                    // prezzo di comodo
        };

        var same = CardScanService.SameKind(offers, "Near Mint", "English", false, myUserId: 99);

        same.Select(o => o.PriceCents).Should().BeEquivalentTo(new[] { 395, 410 });
    }

    [Fact]
    public void DailyCounter_stops_at_the_limit()
    {
        var counter = new ScanDailyCounter();

        counter.TryTake(2, out var first).Should().BeTrue();
        counter.TryTake(2, out var second).Should().BeTrue();
        counter.TryTake(2, out var third).Should().BeFalse();

        (first, second, third).Should().Be((1, 2, 2));
    }

    [Fact]
    public void Parse_normalizes_set_code_and_language()
    {
        var reading = ClaudeCardImageReader.Parse("""
            {"name":"Lightning Bolt","printedName":"Fulmine","setCode":"2X2","collectorNumber":"117",
             "language":"italiano","isFoil":true,"confidence":"high","notes":null}
            """);

        reading.SetCode.Should().Be("2x2");
        reading.Language.Should().Be("Italian");
        reading.IsFoil.Should().BeTrue();
        reading.Notes.Should().BeNull();
    }

    [Fact]
    public void OrderSetCards_sorts_collector_numbers_as_numbers_and_drops_cards_without_one()
    {
        var cards = new[] { "10", "9", "12a", "12", "", "S1", "100" }
            .Select((n, i) => new SetCardDto { BlueprintId = i + 1, CollectorNumber = n, Name = $"Carta {n}" });

        CardScanService.OrderSetCards(cards).Select(c => c.CollectorNumber)
            .Should().Equal("9", "10", "12", "12a", "100", "S1");
    }

    [Theory]
    [InlineData("4ed", "high", false)]
    [InlineData(null, "medium", true)]
    [InlineData("rav", "low", true)]
    public void NeedsFallback_when_the_set_is_missing_or_the_reading_is_unsure(string? set, string confidence, bool expected)
    {
        CardScanService.NeedsFallback(new CardReading { SetCode = set, Confidence = confidence }).Should().Be(expected);
    }

    [Fact]
    public void Rank_without_a_set_puts_collector_reprints_after_the_old_editions()
    {
        var fourth = new Expansion { Id = 10, Name = "Fourth Edition", Code = "4ed", ReleaseDate = new DateTime(1995, 4, 1) };
        var anniversary = new Expansion { Id = 11, Name = "30th Anniversary Edition", Code = "30a", ReleaseDate = new DateTime(2022, 11, 28) };
        var pool = new[] { Card(1, "Death Ward", anniversary, "17"), Card(2, "Death Ward", fourth, "21") };

        var ranked = CardScanService.Rank(new CardReading { Name = "Death Ward" }, pool);

        ranked.Select(c => c.ExpansionCode).Should().Equal("4ed", "30a");
    }

    [Fact]
    public void Rank_keeps_a_collector_reprint_first_when_its_code_was_read()
    {
        var fourth = new Expansion { Id = 10, Name = "Fourth Edition", Code = "4ed", ReleaseDate = new DateTime(1995, 4, 1) };
        var anniversary = new Expansion { Id = 11, Name = "30th Anniversary Edition", Code = "30a", ReleaseDate = new DateTime(2022, 11, 28) };
        var pool = new[] { Card(1, "Death Ward", anniversary, "17"), Card(2, "Death Ward", fourth, "21") };

        var ranked = CardScanService.Rank(new CardReading { Name = "Death Ward", SetCode = "30a", CollectorNumber = "17" }, pool);

        ranked.First().ExpansionCode.Should().Be("30a");
    }
}
