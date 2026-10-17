using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Carrello di un solo venditore (feature 007, "Per venditore"): un traino che paga la spedizione, le altre
/// aperture sopra la soglia, i tenuti a buon prezzo solo insieme a un'apertura. Venditori e prezzi inventati.
/// </summary>
public class SellerBasketBuilderTests
{
    private static readonly SellerRules Rules = new(AnchorRoiPercent: 50m, AddOnRoiPercent: 33m, MinNetPercent: 20m, MaxHoldPremiumPercent: 10m);

    private static SellerProduct Open(int id, decimal value) =>
        new(id, $"Box {id}", "SET", "Apri", value, value * 0.8m, null, false, null, null);

    private static SellerProduct Hold(int id, decimal trend, decimal score = 80m) =>
        new(id, $"Deck {id}", "SET", "Tieni sigillato", trend * 0.95m, trend, score, true, null, null);

    private static SellerOffer Offer(int productId, decimal price, string seller = "VenditoreProva") =>
        new("Cardmarket", seller, null, productId, price, 1, false, null, null);

    [Fact]
    public void Example_of_the_user_anchor_add_ons_and_a_product_to_keep()
    {
        var products = new Dictionary<int, SellerProduct>
        {
            [1] = Open(1, 180m),   // a 100 € rende l'80%: il traino
            [2] = Open(2, 70m),    // a 50 € rende il 40%: entra
            [3] = Open(3, 55m),    // a 50 € rende il 10%: resta fuori
            [4] = Hold(4, 40m)     // da tenere, a 38 € sotto il trend: entra
        };
        var offers = new[] { Offer(1, 100m), Offer(2, 50m), Offer(3, 50m), Offer(4, 38m) };

        var basket = SellerBasketBuilder.Build("cm:venditoreprova", offers, products, 12m, Rules)!;

        basket.Lines.Select(l => l.Product.ProductId).Should().Equal(1, 2, 4);
        basket.OpenCost.Should().Be(150m);
        basket.HoldCost.Should().Be(38m);
        basket.OpenGain.Should().Be(80m + 20m - 12m);
        basket.Total.Should().Be(200m);
    }

    [Fact]
    public void Without_an_anchor_the_seller_is_not_proposed()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 140m), [2] = Open(2, 70m) };

        // 40% e 40%: buone aperture, ma nessuna arriva al 50%.
        SellerBasketBuilder.Build("s", new[] { Offer(1, 100m), Offer(2, 50m) }, products, 7m, Rules).Should().BeNull();
    }

    [Fact]
    public void Products_to_keep_alone_do_not_make_a_basket()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Hold(1, 60m), [2] = Hold(2, 40m) };

        SellerBasketBuilder.Build("s", new[] { Offer(1, 55m), Offer(2, 36m) }, products, 7m, Rules).Should().BeNull();
    }

    [Fact]
    public void A_basket_whose_net_gain_is_too_small_for_the_shipping_is_left_out()
    {
        // Traino al 55% su 20 euro: 11 di guadagno, 12 di spedizione.
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 31m) };

        SellerBasketBuilder.Build("s", new[] { Offer(1, 20m) }, products, 12m, Rules).Should().BeNull();
    }

    [Fact]
    public void A_product_to_keep_above_the_trend_by_more_than_ten_percent_stays_out()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 200m), [2] = Hold(2, 40m) };

        var basket = SellerBasketBuilder.Build("s", new[] { Offer(1, 100m), Offer(2, 45m) }, products, 7m, Rules)!;

        basket.Lines.Should().ContainSingle().Which.Product.ProductId.Should().Be(1);
    }

    [Fact]
    public void Only_the_cheapest_offer_of_each_product_counts()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 200m) };

        var basket = SellerBasketBuilder.Build("s", new[] { Offer(1, 110m), Offer(1, 100m) }, products, 7m, Rules)!;

        basket.Lines.Should().ContainSingle().Which.Offer.Price.Should().Be(100m);
    }

    [Fact]
    public void Sellers_are_ranked_by_net_gain_and_marked_while_they_fit_the_budget()
    {
        SellerBasket Basket(string key, decimal cost, decimal gain) =>
            new(key, "Cardmarket", key, null, 10m, cost, 0m, gain, 0m, 0m, new List<SellerBasketLine>(), false);

        var ranked = SellerBasketBuilder.RankAndFit(new[] { Basket("a", 290m, 40m), Basket("b", 190m, 90m), Basket("c", 90m, 30m) }, 300m);

        ranked.Select(b => (b.Key, b.InBudget)).Should().Equal(("b", true), ("a", false), ("c", true));
    }

    [Fact]
    public void Compose_builds_sellers_ct_zero_and_the_wizards_box()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 180m), [2] = Open(2, 70m) };
        var offers = new List<SellerOffer>
        {
            new("Card Trader Zero", "Alfa", "IT", 1, 100m, 1, true, 11, null),
            new("Card Trader", "Beta", "DE", 2, 50m, 1, false, 12, null),
            new("Card Trader Zero", "Gamma", "FR", 2, 48m, 1, true, 13, null),
            new("Cardmarket", "Delta", null, 1, 95m, 2, false, null, DateTime.UtcNow)
        };
        var wizards = new List<WizardsDrop>
        {
            new("w1", "Drop A", 34.99m, 90m, 150m, "Compra", "u1", new List<string>()),
            new("w2", "Drop B", 44.99m, 80m, 80m, "Compra", "u2", new List<string>())
        };
        var data = new SellerPlanData(DateTime.UtcNow, products, offers, wizards, 3, 0);
        var request = new SellerPlanRequest(500m, 50m, Rules, new BudgetShipping(7m, 8m, 7m, 10m), null);

        var result = SellerPlanService.Compose(data, request);

        // CT Zero mette insieme Alfa (traino) e Gamma (aggiunta) con una spedizione.
        var zero = result.Sellers.Single(s => s.Key == "ctzero");
        zero.Lines.Select(l => l.Offer.Seller).Should().BeEquivalentTo("Alfa", "Gamma");
        zero.Shipping.Should().Be(8m);
        result.Sellers.Should().Contain(s => s.Key == "cm:Delta");
        // Beta ha solo un prodotto al 40%: nessun traino.
        result.Sellers.Should().NotContain(s => s.Key == "ct:Beta");
        // Wizards: 50 euro meno 10 di spedizione bastano per il drop col margine migliore.
        result.Wizards.Where(w => w.Chosen).Select(w => w.Drop.Title).Should().Equal("Drop A");

        SellerPlanService.Compose(data, request with { ExcludedStores = new List<string> { "CardTrader" } }).Sellers
            .Should().ContainSingle().Which.Key.Should().Be("cm:Delta");
    }

    [Fact]
    public void The_basket_stays_inside_the_budget_anchor_first_then_best_add_ons_then_products_to_keep()
    {
        // Prima prova del 17/10: il carrello CT Zero prendeva tutto, 6.783 euro con un budget di 500.
        var products = new Dictionary<int, SellerProduct>
        {
            [1] = Open(1, 300m),    // a 200 €: 50%, traino
            [2] = Open(2, 120m),    // a 60 €: 100%
            [3] = Open(3, 140m),    // a 100 €: 40%
            [4] = Hold(4, 50m)      // a 45 €
        };
        var offers = new[] { Offer(1, 200m), Offer(2, 60m), Offer(3, 100m), Offer(4, 45m) };

        // Budget 300 con 10 di spedizione: il traino al 100% (60) prima, poi il 50% (200); il 40% (100) non ci sta più,
        // il tenuto da 45 neppure (60 + 200 + 10 = 270).
        var basket = SellerBasketBuilder.Build("s", offers, products, 10m, Rules, maxTotal: 300m)!;

        basket.Lines.Select(l => l.Product.ProductId).Should().BeEquivalentTo(new[] { 2, 1 });
        basket.Total.Should().BeLessThanOrEqualTo(300m);
    }

    [Fact]
    public void A_seller_whose_anchor_alone_exceeds_the_budget_is_left_out()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 900m) };

        SellerBasketBuilder.Build("s", new[] { Offer(1, 520m) }, products, 12m, Rules, maxTotal: 500m).Should().BeNull();
    }

    [Fact]
    public void Recompute_takes_the_cardmarket_sellers_pasted_after_the_search()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 180m) };
        var data = new SellerPlanData(DateTime.UtcNow, products,
            new List<SellerOffer> { new("Cardmarket", "Vecchio", null, 1, 100m, 1, false, null, DateTime.UtcNow.AddDays(-2)),
                                    new("Card Trader", "Beta", "DE", 1, 110m, 1, false, 5, null) },
            new List<WizardsDrop>(), 1, 0);

        var updated = SellerPlanService.WithCardmarketOffers(data, new[]
        {
            new ValidSellerOffer(9, "Nuovo", 1, 90m, 2, DateTime.UtcNow),
            new ValidSellerOffer(10, "Altro", 99, 10m, 1, DateTime.UtcNow)   // prodotto fuori dai candidati: ignorato
        });

        updated.Offers.Select(o => o.Seller).Should().BeEquivalentTo("Beta", "Nuovo");
    }

    [Fact]
    public void A_product_to_keep_sealed_bought_well_below_the_trend_opens_profitably()
    {
        // Esempio dell'utente del 17/10: un box del Signore degli Anelli a buon prezzo da un venditore. Al trend conviene
        // rivenderlo chiuso ("Tieni sigillato"), ma a 180 euro aprirlo rende il 67%.
        var products = new Dictionary<int, SellerProduct>
        {
            [1] = new(1, "LTR Box", "LTR", "Tieni sigillato", 300m, 320m, null, false, null, null)
        };

        var basket = SellerBasketBuilder.Build("s", new[] { Offer(1, 180m) }, products, 12m, Rules)!;

        basket.Lines.Should().ContainSingle().Which.Kind.Should().Be(SellerLineKind.Open);
    }

    [Fact]
    public void A_cardmarket_basket_uses_the_highest_shipping_read_from_the_page()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 300m), [2] = Open(2, 80m) };
        var offers = new[]
        {
            new SellerOffer("Cardmarket", "VenditoreProva", null, 1, 150m, 1, false, null, DateTime.UtcNow, 13.69m),
            new SellerOffer("Cardmarket", "VenditoreProva", null, 2, 40m, 1, false, null, DateTime.UtcNow, 8.19m)
        };

        var basket = SellerBasketBuilder.Build("cm:x", offers, products, 12m, Rules)!;

        basket.Shipping.Should().Be(13.69m);
        basket.OpenGain.Should().Be(150m + 40m - 13.69m);
    }

    [Fact]
    public void A_saved_reading_gives_the_same_baskets_after_a_restart()
    {
        var products = new Dictionary<int, SellerProduct> { [1] = Open(1, 180m), [2] = Open(2, 70m) };
        var data = new SellerPlanData(DateTime.UtcNow, products, new List<SellerOffer>
        {
            new("Card Trader Zero", "Alfa", "IT", 1, 100m, 1, true, 11, null),
            new("Cardmarket", "Delta", null, 2, 45m, 2, false, null, DateTime.UtcNow, 8.19m)
        }, new List<WizardsDrop> { new("w1", "Drop A", 34.99m, 90m, 150m, "Compra", "u1", new List<string> { "Preordine" }) }, 3, 0);
        var request = new SellerPlanRequest(500m, 50m, Rules, new BudgetShipping(7m, 8m, 7m, 10m), new List<string>());

        var restoredData = System.Text.Json.JsonSerializer.Deserialize<SellerPlanData>(System.Text.Json.JsonSerializer.Serialize(data))!;
        var restoredRequest = System.Text.Json.JsonSerializer.Deserialize<SellerPlanRequest>(System.Text.Json.JsonSerializer.Serialize(request))!;

        var before = SellerPlanService.Compose(data, request);
        var after = SellerPlanService.Compose(restoredData, restoredRequest);
        after.Sellers.Select(b => (b.Key, b.Total, b.OpenGain)).Should().Equal(before.Sellers.Select(b => (b.Key, b.Total, b.OpenGain)));
        after.Wizards.Should().HaveCount(1);
    }
}
