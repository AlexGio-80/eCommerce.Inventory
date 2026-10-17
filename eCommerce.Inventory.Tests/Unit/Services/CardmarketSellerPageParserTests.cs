using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Pagine delle offerte di un venditore Cardmarket copiate dall'utente (feature 007, Fase 1). Struttura presa da
/// una pagina vera del 17/10; venditore e prezzi inventati (repository pubblico).
/// </summary>
public class CardmarketSellerPageParserTests
{
    private const string MarkdownPage = """
        * [User search](https://www.cardmarket.com/en/Magic/Users)
        * [Advanced Singles Search](https://www.cardmarket.com/en/Magic/AdvancedSearch)
        1. [Home](https://www.cardmarket.com/en/Magic)
        2. [User search](https://www.cardmarket.com/en/Magic/Users)
        3. [VenditoreProva](https://www.cardmarket.com/en/Magic/Users/VenditoreProva)
        4. [Offers](https://www.cardmarket.com/en/Magic/Users/VenditoreProva/Offers)
        5. Preconstructed Decks

        VenditoreProvaPreconstructed Decks
        Filter
        Name
        Commander: FINAL FANTASY (5)Commander: Aetherdrift (1)
        Expansion
        AllEnglish (3)
        English (3)×
        Language!
        3 Hits
        Page 1 of 1
        Name
        Info.
        Offer
         
        [Commander: Aetherdrift: "Living Energy" Commander Deck](https://www.cardmarket.com/en/Magic/Products/x)
        FREE SHIPPING within the EEA on orders over 150 euros!
        30,00 €
        11
         
        [Commander: The Lord of the Rings: Tales of Middle-earth: "Food and Fellowship" Commander Deck](https://www.cardmarket.com/en/Magic/Products/y)
        DAMAGED!
        90,00 €
        4
         
        [Commander: Modern Horizons 3: "Eldrazi Incursion" Commander Deck](https://www.cardmarket.com/en/Magic/Products/z)
        1.140,50 €
        9
        3 Hits
        Page 1 of 1
        """;

    [Fact]
    public void Reads_seller_category_and_every_offer()
    {
        var page = CardmarketSellerPageParser.Parse(MarkdownPage);

        page.Seller.Should().Be("VenditoreProva");
        page.Category.Should().Be("Preconstructed Decks");
        page.EnglishFilter.Should().BeTrue();
        page.Offers.Should().HaveCount(3);

        page.Offers[0].Should().Be(new CardmarketPageOffer(
            "Commander: Aetherdrift: \"Living Energy\" Commander Deck", 30.00m, 11,
            "FREE SHIPPING within the EEA on orders over 150 euros!", false));
        page.Offers[1].IsDamaged.Should().BeTrue();
        page.Offers[2].Should().Be(new CardmarketPageOffer(
            "Commander: Modern Horizons 3: \"Eldrazi Incursion\" Commander Deck", 1140.50m, 9, null, false));
    }

    [Fact]
    public void Reads_plain_text_with_cells_separated_by_tabs()
    {
        var text = "Home\nUser search\nVenditoreProva\nOffers\nSets\n1 Hits\nName\tInfo.\tOffer\n" +
                   "Some Scene Box Set\tnice\t3,96 €\t5\n1 Hits\n";

        var page = CardmarketSellerPageParser.Parse(text);

        page.Seller.Should().Be("VenditoreProva");
        page.Category.Should().Be("Sets");
        page.EnglishFilter.Should().BeFalse();
        page.Offers.Should().ContainSingle().Which.Should().Be(new CardmarketPageOffer("Some Scene Box Set", 3.96m, 5, "nice", false));
    }

    [Fact]
    public void NormalizeName_matches_the_catalog_with_doubled_quotes()
    {
        CardmarketSellerPageParser.NormalizeName("Commander: Aetherdrift: \"\"Living Energy\"\" Commander Deck")
            .Should().Be(CardmarketSellerPageParser.NormalizeName("Commander: Aetherdrift: “Living Energy”  Commander Deck"));
    }
}
