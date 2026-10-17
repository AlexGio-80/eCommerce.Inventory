using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Pianificatore del budget (feature 007): mai oltre il budget, spedizioni comprese; una spedizione per
/// venditore; la divisione fra aprire e tenere; il limite di copie per prodotto.
/// </summary>
public class BudgetPlannerTests
{
    private static readonly Dictionary<string, decimal> Shipping = new()
    {
        ["ct:1"] = 7m, ["ct:2"] = 7m, ["ctzero"] = 8m, ["cm:9"] = 7m, ["wizards"] = 10m
    };

    private static BudgetOffer Open(string key, string product, string seller, decimal price, decimal value, int available = 1, int maxCopies = 3) =>
        new(key, product, seller, BudgetKind.Open, price, value, available, maxCopies);

    private static BudgetOffer Hold(string key, string product, string seller, decimal price, decimal score, int available = 1, int maxCopies = 3) =>
        new(key, product, seller, BudgetKind.Hold, price, score, available, maxCopies);

    [Fact]
    public void Never_spends_more_than_the_budget_shipping_included()
    {
        var offers = new[]
        {
            Open("a", "p1", "ct:1", 60m, 90m, available: 5),
            Open("b", "p2", "ct:2", 45m, 70m, available: 5),
            Open("c", "p3", "cm:9", 30m, 50m)
        };

        var choice = BudgetPlanner.Choose(offers, 150m, 1m, Shipping);

        choice.Spent.Should().BeLessThanOrEqualTo(150m);
        choice.Lines.Should().NotBeEmpty();
    }

    [Fact]
    public void A_second_product_from_the_same_seller_does_not_pay_shipping_again()
    {
        var offers = new[]
        {
            Open("a", "p1", "ct:1", 40m, 60m),
            Open("b", "p2", "ct:1", 40m, 50m, maxCopies: 1),
            Open("c", "p2", "ct:2", 39m, 50m, maxCopies: 1)
        };

        var choice = BudgetPlanner.Choose(offers, 200m, 1m, Shipping);

        // p2 costa un euro meno dal venditore 2, ma la sua spedizione costerebbe 7 euro in più.
        choice.Lines.Select(l => l.Offer.OfferKey).Should().BeEquivalentTo("a", "b");
        choice.ShippingPaid.Should().ContainSingle().Which.Key.Should().Be("ct:1");
    }

    [Fact]
    public void An_offer_whose_gain_does_not_cover_shipping_is_left_out()
    {
        var choice = BudgetPlanner.Choose(new[] { Open("a", "p1", "ct:1", 50m, 55m) }, 500m, 1m, Shipping);

        choice.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Copies_are_limited_per_product_across_offers()
    {
        var offers = new[]
        {
            Open("a", "p1", "ct:1", 20m, 60m, available: 5, maxCopies: 2),
            Open("b", "p1", "ctzero", 21m, 60m, available: 5, maxCopies: 2)
        };

        var choice = BudgetPlanner.Choose(offers, 1000m, 1m, Shipping);

        choice.Lines.Sum(l => l.Quantity).Should().Be(2);
    }

    [Fact]
    public void Each_part_of_the_budget_goes_to_its_kind_and_what_is_left_moves_on()
    {
        var offers = new[]
        {
            Open("o1", "p1", "ct:1", 50m, 100m, available: 10, maxCopies: 10),
            Hold("h1", "p2", "ct:2", 25m, 80m)
        };

        // 70 euro per aprire bastano per una copia (50 + 7); 30 per tenere bastano per il tenuto (25 + 7 no:
        // passa con l'avanzo di aprire, 13 euro).
        var choice = BudgetPlanner.Choose(offers, 100m, 0.7m, Shipping);

        choice.Lines.Should().Contain(l => l.Offer.OfferKey == "h1");
        choice.Lines.Single(l => l.Offer.OfferKey == "o1").Quantity.Should().Be(1);
        choice.Spent.Should().BeLessThanOrEqualTo(100m);
    }

    [Fact]
    public void Hold_prefers_the_higher_score()
    {
        var offers = new[]
        {
            Hold("h1", "p1", "ct:1", 40m, 70m),
            Hold("h2", "p2", "ct:1", 40m, 90m)
        };

        var choice = BudgetPlanner.Choose(offers, 50m, 0m, Shipping);

        choice.Lines.Should().ContainSingle().Which.Offer.OfferKey.Should().Be("h2");
    }

    [Fact]
    public void Excluded_offers_are_skipped()
    {
        var offers = new[] { Open("a", "p1", "ct:1", 40m, 80m), Open("b", "p1", "ct:2", 42m, 80m) };

        var choice = BudgetPlanner.Choose(offers, 100m, 1m, Shipping, new[] { "a" });

        choice.Lines.Should().ContainSingle().Which.Offer.OfferKey.Should().Be("b");
    }

    [Theory]
    [InlineData(10, 20, 3, 2)]    // troppo poche copie per misurare
    [InlineData(600, 400, 3, 3)]  // 60% venduto
    [InlineData(300, 700, 3, 2)]  // 30%
    [InlineData(100, 900, 3, 1)]  // 10%
    public void CopyLimit_follows_the_sell_through_of_the_release(int sold, int inStock, int max, int expected)
    {
        BudgetPlanService.CopyLimit(sold, inStock, max).Should().Be(expected);
    }

    [Fact]
    public void Flags_warn_about_falling_prices_expensive_history_and_recent_releases()
    {
        var row = new OpportunityRow(1, "Box", "FIN", null, null, 100m, 95m, 120m, 20m, "Apri", null, null,
            ReleaseAgeMonths: 3, HistoryPosition: 10m, IsHoldCandidate: false);

        var flags = BudgetPlanService.Flags(row, -8m);

        flags.Should().HaveCount(3);
        BudgetPlanService.Flags(row with { ReleaseAgeMonths = 20, HistoryPosition = 80m }, 2m).Should().BeEmpty();
    }

    [Fact]
    public void Hold_skips_an_offer_whose_shipping_makes_it_cost_more_than_the_market()
    {
        // Prima prova del 17/10: mazzo da 15 euro di trend, 9,99 più 12 di spedizione.
        var shipping = new Dictionary<string, decimal> { ["cm:1"] = 12m, ["ct:1"] = 7m };
        var offers = new[]
        {
            new BudgetOffer("cm", "p1", "cm:1", BudgetKind.Hold, 9.99m, 85m, 1, 2, MaxCost: 16.5m),
            new BudgetOffer("big", "p2", "ct:1", BudgetKind.Hold, 60m, 80m, 1, 1, MaxCost: 70m),
            new BudgetOffer("ct", "p1", "ct:1", BudgetKind.Hold, 14m, 85m, 1, 2, MaxCost: 16.5m)
        };

        var choice = BudgetPlanner.Choose(offers, 200m, 0m, shipping);

        // Da Cardmarket mai; dal venditore Card Trader sì, perché la sua spedizione è già pagata dal box da tenere.
        choice.Lines.Select(l => l.Offer.OfferKey).Should().BeEquivalentTo("big", "ct");
    }

    [Theory]
    [InlineData(172.74, 80.00, 172.74)]   // il prezzo più basso, in qualsiasi lingua, non è un prezzo a cui comprare
    [InlineData(79.36, 85.00, 85.00)]     // nessuno vende sotto il trend: si compra al più basso
    public void CardmarketBuyPrice_is_the_trend_or_the_lowest_offer_if_higher(double trend, double low, double expected)
    {
        var row = new OpportunityRow(1, "Box", "BLB", null, 1, (decimal)trend, (decimal)low, 200m, 20m, "Apri", null, null, 12, 50m, false);

        BudgetPlanService.CardmarketBuyPrice(row).Should().Be((decimal)expected);
    }

    [Fact]
    public void Hold_spreads_the_budget_over_more_products_when_capped()
    {
        // Prova del 17/10: con 500 euro tutto tenere, due prodotti si prendevano 434 euro.
        var offers = new[]
        {
            Hold("big", "p1", "ct:1", 250m, 90m),
            Hold("m1", "p2", "ct:1", 80m, 80m),
            Hold("m2", "p3", "ct:1", 70m, 78m),
            Hold("m3", "p4", "ct:1", 60m, 75m)
        };

        var choice = BudgetPlanner.Choose(offers, 500m, 0m, Shipping, maxHoldProductShare: 0.4m);

        choice.Lines.Select(l => l.Offer.OfferKey).Should().BeEquivalentTo("m1", "m2", "m3");
    }

    [Fact]
    public void PastedKind_opens_at_the_seller_price_or_holds_a_candidate()
    {
        var open = new OpportunityRow(1, "Deck", "DFT", null, 1, 37m, 33m, 50m, 5m, "Apri", null, null, 12, 50m, false);
        var hold = open with { Decision = "Tieni sigillato", OpenValueCm = 42m, HoldScore = 87m, IsHoldCandidate = true };

        // In classifica rende il 5% al trend; al prezzo del venditore, 40 euro, il 25%.
        BudgetPlanService.PastedKind(open, 40m, 10m, 300m).Should().Be(BudgetKind.Open);
        BudgetPlanService.PastedKind(open, 48m, 10m, 300m).Should().BeNull();
        BudgetPlanService.PastedKind(hold, 40m, 10m, 300m).Should().Be(BudgetKind.Hold);
    }

    [Fact]
    public void Compose_leaves_out_the_stores_switched_off()
    {
        BudgetOfferInfo Info(string name, BudgetKind kind, string store) =>
            new(1, name, null, kind, store, null, null, 100m, 50m, null, null, new List<string>(), null, null, null, null, null);
        var data = new BudgetPlanData(DateTime.UtcNow, new BudgetPlanRequest(500m, 100, false, null),
            new List<BudgetOffer>
            {
                new("wz:1", "wz:1", "wizards", BudgetKind.SecretLair, 35m, 120m, 1, 1),
                new("cms:7", "p:2", "cms:venditore", BudgetKind.Open, 40m, 60m, 3, 3)
            },
            new Dictionary<string, BudgetOfferInfo>
            {
                ["wz:1"] = Info("Drop", BudgetKind.SecretLair, "Wizards"),
                ["cms:7"] = Info("Deck", BudgetKind.Open, "Cardmarket")
            },
            1, 0, 1, 0, 40m);
        var shipping = new BudgetShipping(7m, 8m, 7m, 10m);

        BudgetPlanService.Compose(data, shipping, Array.Empty<string>()).Baskets.Select(b => b.Store)
            .Should().BeEquivalentTo("Wizards", "Cardmarket");
        BudgetPlanService.Compose(data, shipping, Array.Empty<string>(), new[] { "Wizards" }).Baskets
            .Should().ContainSingle().Which.Lines.Single().Quantity.Should().Be(3);
    }
}
