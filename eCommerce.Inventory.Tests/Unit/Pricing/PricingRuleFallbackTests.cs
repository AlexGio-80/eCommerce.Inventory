using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Pricing;

/// <summary>
/// Regole a ripiego: più regole sulla stessa fascia si provano in ordine di priorità, e si passa
/// alla successiva solo quando il mercato non basta per la precedente.
/// </summary>
public class PricingRuleFallbackTests
{
    private const int MyUserId = 1939;

    private static PricingProfile Profile(bool onlyCtZero, int minOffers, params PricingRule[] rules)
    {
        var profile = new PricingProfile
        {
            Name = "Test",
            DryRun = false,
            MinPrice = 0.05m,
            MaxIncreasePercentPerRun = 100000m,
            MaxDecreasePercentPerRun = 100000m,
            MaxMedianRatio = 0m,
            MinComparableOffers = minOffers,
            MinOffersForOutlierRejection = 5,
            EnableOutlierRejection = false,
            IncludeOnlyCtZeroSellers = onlyCtZero,
            SkipWhenFewerOffersThanPosition = true
        };

        foreach (var r in rules) profile.Rules.Add(r);
        return profile;
    }

    private static PricingRule Rule(int id, int priority, int position, int? minOffers = null, bool? onlyCtZero = null)
        => new()
        {
            Id = id,
            FromPrice = 1m,
            ToPrice = 25m,
            ReferenceMode = PriceReferenceMode.NthLowestOffer,
            Position = position,
            Priority = priority,
            MinComparableOffers = minOffers,
            OnlyCtZeroSellers = onlyCtZero,
            CanIncrease = true,
            CanDecrease = true,
            IsActive = true
        };

    private static InventoryItem Item(decimal price) => new()
    {
        Id = 1,
        BlueprintId = 10,
        ListingPrice = price,
        Condition = "Near Mint",
        Language = "English",
        Quantity = 1
    };

    private static CardTraderMarketplaceProductDto Offer(decimal price, bool ctZero) => new()
    {
        Id = Random.Shared.Next(1, 100000),
        PriceCents = (int)(price * 100),
        Quantity = 1,
        PropertiesHash = new Dictionary<string, object>
        {
            ["condition"] = "Near Mint",
            ["mtg_language"] = "en",
            ["mtg_foil"] = false,
            ["signed"] = false,
            ["altered"] = false
        },
        User = new CardTraderMarketplaceUserDto
        {
            Id = Random.Shared.Next(2000, 100000),
            UserType = "normal",
            CountryCode = "IT",
            MaxSellableIn24hQuantity = 10,
            CanSellViaHub = ctZero
        }
    };

    /// <summary>Due offerte Card Trader Zero e tre di venditori normali.</summary>
    private static List<CardTraderMarketplaceProductDto> ThinCtZeroMarket() => new()
    {
        Offer(10m, ctZero: true),
        Offer(11m, ctZero: true),
        Offer(8m, ctZero: false),
        Offer(9m, ctZero: false),
        Offer(12m, ctZero: false)
    };

    [Fact]
    public void Con_poche_offerte_Card_Trader_Zero_passa_al_ripiego_su_tutti_i_venditori()
    {
        var main = Rule(1, priority: 0, position: 2);
        var fallback = Rule(2, priority: 1, position: 2, minOffers: 2, onlyCtZero: false);
        var profile = Profile(onlyCtZero: true, minOffers: 4, main, fallback);

        var decision = new PricingEngine().Evaluate(Item(5m), ThinCtZeroMarket(), profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.Applied);
        decision.RuleId.Should().Be(2);
        decision.ReferencePrice.Should().Be(9m, "la seconda più bassa fra tutti i venditori: 8, 9, 10, 11, 12");
        decision.ComparableOffersCount.Should().Be(5);
        decision.Reason.Should().Contain("Regola di ripiego 1").And.Contain("il minimo richiesto è 4");
    }

    [Fact]
    public void Se_la_regola_principale_ha_mercato_il_ripiego_non_si_usa()
    {
        var main = Rule(1, priority: 0, position: 1);
        var fallback = Rule(2, priority: 1, position: 1, minOffers: 1, onlyCtZero: false);
        var profile = Profile(onlyCtZero: true, minOffers: 2, main, fallback);

        var decision = new PricingEngine().Evaluate(Item(5m), ThinCtZeroMarket(), profile, MyUserId);

        decision.RuleId.Should().Be(1);
        decision.ReferencePrice.Should().Be(10m, "la più bassa delle sole offerte Card Trader Zero (10, 11), non l'8 dei venditori normali");
        decision.Reason.Should().NotContain("ripiego");
    }

    [Fact]
    public void Senza_mercato_per_nessuna_regola_riporta_tutti_i_motivi()
    {
        var main = Rule(1, priority: 0, position: 2);
        var fallback = Rule(2, priority: 1, position: 2, minOffers: 6, onlyCtZero: false);
        var profile = Profile(onlyCtZero: true, minOffers: 4, main, fallback);

        var decision = new PricingEngine().Evaluate(Item(5m), ThinCtZeroMarket(), profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.InsufficientOffers);
        decision.ProposedPrice.Should().Be(5m);
        decision.Reason.Should().Contain("Regola principale: Solo 2 offerte comparabili, il minimo richiesto è 4")
            .And.Contain("Ripiego 1: Solo 5 offerte comparabili, il minimo richiesto è 6");
    }

    [Fact]
    public void Il_ripiego_scatta_anche_quando_manca_la_posizione_richiesta()
    {
        var main = Rule(1, priority: 0, position: 6);
        var fallback = Rule(2, priority: 1, position: 3);
        var profile = Profile(onlyCtZero: false, minOffers: 1, main, fallback);

        var decision = new PricingEngine().Evaluate(Item(5m), ThinCtZeroMarket(), profile, MyUserId);

        decision.RuleId.Should().Be(2);
        decision.ReferencePrice.Should().Be(10m);
        decision.Reason.Should().Contain("chiede la posizione 6");
    }

    [Fact]
    public void Il_guardrail_della_regola_principale_non_fa_scattare_il_ripiego()
    {
        // Il ripiego serve ai mercati sottili: un blocco del guardrail è una decisione presa su
        // un mercato sufficiente, e cercare un'altra regola per aggirarlo lo svuoterebbe di senso.
        var main = Rule(1, priority: 0, position: 1);
        var fallback = Rule(2, priority: 1, position: 3);
        var profile = Profile(onlyCtZero: false, minOffers: 1, main, fallback);
        profile.MaxDecreasePercentPerRun = 10m;
        profile.GuardrailExemptAmount = 0m;

        var decision = new PricingEngine().Evaluate(Item(20m), ThinCtZeroMarket(), profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.BlockedByGuardrail);
        decision.RuleId.Should().Be(1);
    }

    [Fact]
    public void Il_minimo_della_regola_prevale_su_quello_del_profilo()
    {
        var profile = Profile(onlyCtZero: false, minOffers: 1, Rule(1, priority: 0, position: 1, minOffers: 6));

        var decision = new PricingEngine().Evaluate(Item(5m), ThinCtZeroMarket(), profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.InsufficientOffers);
        decision.Reason.Should().Be("Solo 5 offerte comparabili, il minimo richiesto è 6");
    }

    [Fact]
    public void Le_regole_disattivate_non_entrano_nella_catena()
    {
        var main = Rule(1, priority: 0, position: 2);
        var fallback = Rule(2, priority: 1, position: 2, minOffers: 2, onlyCtZero: false);
        fallback.IsActive = false;
        var profile = Profile(onlyCtZero: true, minOffers: 4, main, fallback);

        var decision = new PricingEngine().Evaluate(Item(5m), ThinCtZeroMarket(), profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.InsufficientOffers);
        decision.Reason.Should().Be("Solo 2 offerte comparabili, il minimo richiesto è 4 (solo venditori Card Trader Zero)");
    }
}
