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
/// «Applica comunque» dalla scheda Storico: la stessa strada di «Applica dall'anteprima»
/// (rivaluta su dati freschi, scrive anche in dry-run), con in più l'ignorare il guardrail
/// per le carte scelte. Va verificato che il bypass scriva quando richiesto, e che senza
/// non cambi nulla rispetto a un'esecuzione normale — il guardrail protegge tutte le altre carte.
/// </summary>
public class ApplicaComunqueTests
{
    private const int MyUserId = 1939;
    private const int ProdottoSuCardTrader = 777;

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CardTraderMarketplaceProductDto Offerta(decimal price, int userId = 999)
        => new()
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

    /// <summary>
    /// Magazzino minimo con un ribasso che il guardrail blocca: mercato a 5 €, mia inserzione
    /// a 20 €, ribasso massimo per esecuzione al 50%.
    /// </summary>
    private static async Task<(AutoPricingService Service, PricingProfile Profile, Mock<ICardTraderApiService> Api)>
        PredisponiAsync()
    {
        var context = CreateContext();

        var profile = new PricingProfile
        {
            Name = "Profilo predefinito",
            IsActive = true,
            DryRun = false,
            MinPrice = 0.05m,
            MaxIncreasePercentPerRun = 100000m,
            MaxDecreasePercentPerRun = 50m, // scatta: il ribasso reale è del 75%
            MaxMedianRatio = 0m,
            MinComparableOffers = 1,
            EnableOutlierRejection = false,
            Rules =
            {
                new PricingRule
                {
                    FromPrice = 0.02m, ToPrice = 1000m,
                    ReferenceMode = PriceReferenceMode.NthLowestOffer,
                    Position = 1, CanIncrease = true, CanDecrease = true, IsActive = true
                }
            }
        };
        context.PricingProfiles.Add(profile);

        context.Blueprints.Add(new Blueprint
        {
            Id = 10, CardTraderId = 4242, Name = "Carta di prova", Version = "1"
        });
        context.InventoryItems.Add(new InventoryItem
        {
            Id = 1,
            BlueprintId = 10,
            CardTraderProductId = ProdottoSuCardTrader,
            ListingPrice = 20.00m,
            Quantity = 1,
            Condition = "Near Mint",
            Language = "English",
            IsFoil = false,
            Location = ""
        });
        await context.SaveChangesAsync();

        var api = new Mock<ICardTraderApiService>();
        api.Setup(a => a.GetMarketplaceProductsAsync(4242, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CardTraderMarketplaceProductDto>
            {
                Offerta(20.00m, MyUserId), // la mia: dà il fattore di conversione fra le due scale
                Offerta(5.00m),
                Offerta(6.00m)
            });
        api.Setup(a => a.UpdateProductPriceAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CardTraderApi:UserId"] = MyUserId.ToString() })
            .Build();

        var service = new AutoPricingService(
            context, api.Object, new PricingEngine(), configuration,
            NullLogger<AutoPricingService>.Instance);

        return (service, profile, api);
    }

    [Fact]
    public async Task Senza_bypass_il_guardrail_blocca_come_sempre()
    {
        var (service, profile, api) = await PredisponiAsync();

        var run = await service.RunAsync(
            new[] { 10 }, profile, PricingTrigger.Manual,
            forceDryRun: false, refreshPricesFirst: false,
            forceApply: true);

        run.AppliedCount.Should().Be(0);
        run.SkippedCount.Should().Be(1);
        api.Verify(a => a.UpdateProductPriceAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Con_bypass_scrive_nonostante_il_guardrail()
    {
        var (service, profile, api) = await PredisponiAsync();

        var run = await service.RunAsync(
            new[] { 10 }, profile, PricingTrigger.Manual,
            forceDryRun: false, refreshPricesFirst: false,
            forceApply: true, bypassGuardrail: true);

        run.AppliedCount.Should().Be(1);
        run.SkippedCount.Should().Be(0);
        api.Verify(a => a.UpdateProductPriceAsync(
            ProdottoSuCardTrader, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
