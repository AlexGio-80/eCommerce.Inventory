using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Pricing;

/// <summary>
/// Carte a cui l'autopricer non ha mai scritto né confermato un prezzo: il prezzo è quello di
/// caricamento, spesso alto di proposito. La fascia si ricalcola sul prezzo proposto, e
/// guardrail e direzione non si applicano.
/// </summary>
public class InserzioniNuoveTests
{
    private const int MyUserId = 1939;

    private static PricingRule Rule(int id, decimal from, decimal to, int position, decimal adjust, int? minOffers = null)
        => new()
        {
            Id = id,
            FromPrice = from,
            ToPrice = to,
            ReferenceMode = PriceReferenceMode.NthLowestOffer,
            Position = position,
            AdjustmentAmount = adjust,
            Priority = id,
            MinComparableOffers = minOffers,
            CanIncrease = true,
            CanDecrease = true,
            IsActive = true
        };

    /// <summary>Profilo come quello reale: bulk alla 2ª offerta −0,01, 1–25 € alla 2ª −0,10, ribasso massimo 80%.</summary>
    private static PricingProfile Profile(PricingRule? bulk = null)
    {
        var profile = new PricingProfile
        {
            Name = "Test",
            DryRun = false,
            MinPrice = 0.05m,
            MaxIncreasePercentPerRun = 10000m,
            MaxDecreasePercentPerRun = 80m,
            GuardrailExemptAmount = 0.15m,
            MaxMedianRatio = 0m,
            MinComparableOffers = 1,
            EnableOutlierRejection = false,
            SkipWhenFewerOffersThanPosition = true
        };
        profile.Rules.Add(bulk ?? Rule(1, 0.02m, 1m, position: 2, adjust: -0.01m));
        profile.Rules.Add(Rule(2, 1.01m, 25m, position: 2, adjust: -0.10m));
        return profile;
    }

    private static InventoryItem Item(decimal price) => new()
    {
        Id = 1,
        BlueprintId = 10,
        ListingPrice = price,
        Condition = "Near Mint",
        Language = "English",
        Quantity = 1,
        Location = ""
    };

    private static CardTraderMarketplaceProductDto Offer(decimal price, int userId = 999) => new()
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
            Id = userId,
            UserType = "normal",
            CountryCode = "IT",
            MaxSellableIn24hQuantity = 10
        }
    };

    /// <summary>Mercato da bulk: in vetrina 0,30 / 0,40 / 0,50 €.</summary>
    private static List<CardTraderMarketplaceProductDto> BulkMarket() => new()
    {
        Offer(0.30m, userId: 2001),
        Offer(0.40m, userId: 2002),
        Offer(0.50m, userId: 2003)
    };

    [Fact]
    public void Carta_nuova_caricata_alta_prende_la_regola_della_fascia_del_suo_valore()
    {
        // Caricata a 5 €: la fascia del prezzo di caricamento è 1–25 € (2ª offerta −0,10), che
        // propone 0,40 − 0,10 − 0,09 = 0,21 €. Quel prezzo sta nel bulk, e la regola del bulk
        // (2ª offerta −0,01) dà 0,40 − 0,01 − 0,09 = 0,30 €.
        var decision = new PricingEngine().Evaluate(Item(5m), BulkMarket(), Profile(), MyUserId, isNewListing: true);

        decision.Outcome.Should().Be(PricingOutcome.Applied);
        decision.RuleId.Should().Be(1);
        decision.ProposedPrice.Should().Be(0.30m);
        decision.Reason.Should().Contain("Inserzione nuova").And.Contain("fascia scelta sul prezzo di mercato stimato (0,21 €)");
    }

    [Fact]
    public void La_stessa_carta_gia_prezzata_resta_protetta_dal_guardrail()
    {
        var decision = new PricingEngine().Evaluate(Item(5m), BulkMarket(), Profile(), MyUserId, isNewListing: false);

        decision.Outcome.Should().Be(PricingOutcome.BlockedByGuardrail);
        decision.RuleId.Should().Be(2, "senza ricalcolo la fascia resta quella del prezzo attuale");
    }

    [Fact]
    public void Se_la_fascia_del_valore_non_ha_mercato_resta_il_primo_calcolo()
    {
        // La regola del bulk chiede 10 offerte: il ricalcolo non è possibile, ma il primo prezzo
        // proposto è comunque un prezzo di mercato, meglio dei 5 € di caricamento.
        var profile = Profile(bulk: Rule(1, 0.02m, 1m, position: 2, adjust: -0.01m, minOffers: 10));

        var decision = new PricingEngine().Evaluate(Item(5m), BulkMarket(), profile, MyUserId, isNewListing: true);

        decision.Outcome.Should().Be(PricingOutcome.Applied);
        decision.RuleId.Should().Be(2);
        decision.ProposedPrice.Should().Be(0.21m);
        decision.Reason.Should().NotContain("fascia scelta");
    }

    [Fact]
    public void Sulla_carta_nuova_la_direzione_della_regola_non_blocca()
    {
        var profile = Profile();
        foreach (var r in profile.Rules) r.CanDecrease = false;

        var decision = new PricingEngine().Evaluate(Item(5m), BulkMarket(), profile, MyUserId, isNewListing: true);

        decision.Outcome.Should().Be(PricingOutcome.Applied);
        decision.ProposedPrice.Should().Be(0.30m);
    }

    [Fact]
    public void Carta_nuova_gia_nella_fascia_giusta_non_ricalcola()
    {
        var market = new List<CardTraderMarketplaceProductDto> { Offer(3m, 2001), Offer(4m, 2002), Offer(5m, 2003) };

        var decision = new PricingEngine().Evaluate(Item(20m), market, Profile(), MyUserId, isNewListing: true);

        decision.Outcome.Should().Be(PricingOutcome.Applied, "−81% bloccato dal guardrail su una carta già prezzata");
        decision.RuleId.Should().Be(2);
        decision.ProposedPrice.Should().Be(3.81m);
        decision.Reason.Should().NotContain("fascia scelta");
    }

    // --- Dallo storico al motore ---

    private static async Task<(AutoPricingService Service, PricingProfile Profile, ApplicationDbContext Context)> PredisponiAsync(bool giaPrezzata)
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var profile = Profile();
        profile.IsActive = true;
        context.PricingProfiles.Add(profile);
        context.Blueprints.Add(new Blueprint { Id = 10, CardTraderId = 4242, Name = "Carta di prova", Version = "1" });
        var item = Item(5m);
        item.CardTraderProductId = 777;
        context.InventoryItems.Add(item);

        if (giaPrezzata)
        {
            context.PriceChangeLogs.Add(new PriceChangeLog
            {
                InventoryItemId = 1, BlueprintId = 10, OldPrice = 5m, ProposedPrice = 5m,
                Outcome = PricingOutcome.NoChangeNeeded, Trigger = PricingTrigger.Scheduled, Reason = "notturna precedente"
            });
        }
        else
        {
            // Un tentativo non riuscito non rende la carta "prezzata".
            context.PriceChangeLogs.Add(new PriceChangeLog
            {
                InventoryItemId = 1, BlueprintId = 10, OldPrice = 5m, ProposedPrice = 5m,
                Outcome = PricingOutcome.InsufficientOffers, Trigger = PricingTrigger.ListingCreated, Reason = "poche offerte"
            });
        }

        await context.SaveChangesAsync();

        var api = new Mock<ICardTraderApiService>();
        api.Setup(a => a.GetMarketplaceProductsAsync(4242, It.IsAny<CancellationToken>())).ReturnsAsync(BulkMarket());
        api.Setup(a => a.UpdateProductPriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CardTraderApi:UserId"] = MyUserId.ToString() })
            .Build();

        var service = new AutoPricingService(context, api.Object, new PricingEngine(), configuration,
            NullLogger<AutoPricingService>.Instance);

        return (service, profile, context);
    }

    [Fact]
    public async Task La_notturna_tratta_da_nuova_una_carta_mai_prezzata()
    {
        var (service, profile, context) = await PredisponiAsync(giaPrezzata: false);

        var run = await service.RunAsync(new[] { 10 }, profile, PricingTrigger.Scheduled, refreshPricesFirst: false);

        run.AppliedCount.Should().Be(1);
        (await context.InventoryItems.SingleAsync()).ListingPrice.Should().Be(0.30m);
    }

    [Fact]
    public async Task La_notturna_protegge_una_carta_gia_prezzata()
    {
        var (service, profile, context) = await PredisponiAsync(giaPrezzata: true);

        var run = await service.RunAsync(new[] { 10 }, profile, PricingTrigger.Scheduled, refreshPricesFirst: false);

        run.AppliedCount.Should().Be(0);
        (await context.InventoryItems.SingleAsync()).ListingPrice.Should().Be(5m);
    }
}
