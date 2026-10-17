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

    [Fact]
    public void Reads_the_page_number()
    {
        CardmarketSellerPageParser.Parse(MarkdownPage).Page.Should().Be(1);
        CardmarketSellerPageParser.Parse("Name\nInfo.\nOffer\nX\n1,00 €\n1\n").Page.Should().BeNull();
    }

    [Fact]
    public void Reads_price_and_shipping_when_the_page_shows_the_shipping_under_the_price()
    {
        // Prima prova del 17/10: da utente collegato sotto il prezzo c'è la spedizione, che veniva letta come prezzo.
        var text = "Home\nUser search\nVenditoreProva\nOffers\nSets\nName\nInfo.\nOffer\n" +
                   "Universes Beyond: Fallout: Deck Set\n248,69 €\n13,69 €\n1\n" +
                   "The Hobbit Draft Night\nH01&\n124,69 €\n13,69 €\n5\n" +
                   "Some Scene Box\n20,69 €\n4\n" +
                   "2 Hits\n";

        var page = CardmarketSellerPageParser.Parse(text);

        page.Offers.Should().Equal(
            new CardmarketPageOffer("Universes Beyond: Fallout: Deck Set", 248.69m, 1, null, false, 13.69m),
            new CardmarketPageOffer("The Hobbit Draft Night", 124.69m, 5, "H01&", false, 13.69m),
            new CardmarketPageOffer("Some Scene Box", 20.69m, 4, null, false, null));
    }

    [Fact]
    public void Ignores_the_cart_quantity_menu_copied_with_the_page()
    {
        // Prova del 17/10: da utente collegato il menu "1" accanto a ogni offerta finiva come nome del prodotto dopo.
        var text = "Home\nUser search\nVenditoreProva\nOffers\nBooster Boxes\nPage 1 of 1\nName\nInfo.\nOffer\n" +
                   "Commander Masters Set Booster Box\nFREE SHIPPING\n599,99 €\n2\n1\n" +
                   "Magic: The Gathering Foundations Jumpstart Booster Box\nFREE SHIPPING\n94,99 €\n6,70 €\n1\n" +
                   "The Hobbit Play Booster Box\nFREE SHIPPING\n164,99 €\n6\n1\n" +
                   "8 Hits\n";

        var page = CardmarketSellerPageParser.Parse(text);

        page.Offers.Select(o => (o.Name, o.Price, o.Quantity)).Should().Equal(
            ("Commander Masters Set Booster Box", 599.99m, 2),
            ("Magic: The Gathering Foundations Jumpstart Booster Box", 94.99m, 1),
            ("The Hobbit Play Booster Box", 164.99m, 6));
    }
}
