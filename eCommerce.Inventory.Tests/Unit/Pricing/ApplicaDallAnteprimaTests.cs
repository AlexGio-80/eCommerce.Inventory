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
/// «Applica» dall'anteprima scrive i prezzi anche con il profilo in dry-run: è un gesto
/// esplicito su carte appena esaminate una per una, ed è il modo di uscire dalla simulazione
/// un pezzo alla volta senza attivare la scrittura anche sull'esecuzione notturna.
///
/// Proprio perché è l'unica strada che scavalca il dry-run, va verificata in entrambi i versi:
/// che scriva quando glielo si chiede, e che nessun'altra strada scriva per sbaglio. Il costo
/// di un errore qui non è simmetrico — un prezzo sbagliato pubblicato viene comprato subito.
/// </summary>
public class ApplicaDallAnteprimaTests
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
    /// Un magazzino minimo con una carta sopravvalutata: il mercato sta a 5 €, la mia
    /// inserzione a 20 €, quindi la valutazione produce senz'altro una variazione.
    /// </summary>
    private static async Task<(AutoPricingService Service, PricingProfile Profile, Mock<ICardTraderApiService> Api)>
        PredisponiAsync(bool profiloInDryRun)
    {
        var context = CreateContext();

        var profile = new PricingProfile
        {
            Name = "Profilo predefinito",
            IsActive = true,
            DryRun = profiloInDryRun,
            MinPrice = 0.05m,
            MaxIncreasePercentPerRun = 100000m,
            MaxDecreasePercentPerRun = 100000m,
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
                Offerta(6.00m),
                Offerta(7.00m)
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
    public async Task Applica_scrive_su_Card_Trader_anche_con_il_profilo_in_dry_run()
    {
        var (service, profile, api) = await PredisponiAsync(profiloInDryRun: true);

        var run = await service.RunAsync(
            new[] { 10 }, profile, PricingTrigger.Manual,
            forceDryRun: false, refreshPricesFirst: false,
            forceApply: true);

        run.DryRun.Should().BeFalse();
        run.AppliedCount.Should().Be(1);
        api.Verify(a => a.UpdateProductPriceAsync(
            ProdottoSuCardTrader, It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Senza_applicazione_esplicita_il_dry_run_del_profilo_continua_a_valere()
    {
        // È il comportamento normale, quello dell'esecuzione notturna e di quella manuale:
        // il dry-run resta una proprietà del profilo e nessuno lo scavalca di nascosto.
        var (service, profile, api) = await PredisponiAsync(profiloInDryRun: true);

        var run = await service.RunAsync(
            new[] { 10 }, profile, PricingTrigger.Manual,
            forceDryRun: false, refreshPricesFirst: false);

        run.DryRun.Should().BeTrue();
        run.AppliedCount.Should().Be(0);
        run.SimulatedCount.Should().Be(1);
        api.Verify(a => a.UpdateProductPriceAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task L_anteprima_non_scrive_mai_nemmeno_se_le_si_chiede_di_applicare()
    {
        // I due interruttori sono in conflitto solo per errore di programmazione, ma il verso
        // in cui va risolto non è opinabile: l'anteprima è lo strumento che si usa per provare,
        // e deve restare innocuo per costruzione.
        var (service, profile, api) = await PredisponiAsync(profiloInDryRun: false);

        var run = await service.RunAsync(
            new[] { 10 }, profile, PricingTrigger.Preview,
            forceDryRun: true, refreshPricesFirst: false,
            forceApply: true);

        run.DryRun.Should().BeTrue();
        api.Verify(a => a.UpdateProductPriceAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
