using System.Text.Json;
using eCommerce.Inventory.Api;
using eCommerce.Inventory.Api.Controllers;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Controllers;

/// <summary>
/// Salvare il profilo dalla scheda Regole falliva: l'API scrive <c>referenceMode</c> come
/// stringa (le mappature usano <c>ToString()</c>) ma non era configurata per rileggerlo,
/// quindi il corpo rimandato indietro dall'interfaccia non superava il binding.
///
/// Il test parte dal JSON esatto che manda l'interfaccia e lo deserializza con la
/// configurazione reale dell'API, non con una copia: è l'unico modo perché una futura
/// modifica a <see cref="ApiJsonOptions"/> rompa il test invece della maschera.
/// </summary>
public class PricingProfileSaveTests
{
    /// <summary>Corpo inviato da <c>PricingPageComponent.save()</c>: il profilo intero, regole comprese.</summary>
    private const string PayloadDallaMaschera = """
    {
      "id": 1,
      "name": "Profilo predefinito",
      "isActive": true,
      "dryRun": true,
      "minPrice": 0.02,
      "maxIncreasePercentPerRun": 200,
      "maxDecreasePercentPerRun": 15,
      "maxMedianRatio": 3,
      "includeProSellers": true,
      "includeNormalSellers": true,
      "excludeVacationSellers": true,
      "minSellerDailyCapacity": null,
      "countryCodesCsv": "IT",
      "enableOutlierRejection": true,
      "outlierMadThreshold": 3,
      "minOffersForOutlierRejection": 5,
      "minComparableOffers": 3,
      "matchCondition": true,
      "matchLanguage": true,
      "matchFoil": true,
      "rules": [
        {
          "id": 10,
          "fromPrice": 0.02,
          "toPrice": 1,
          "referenceMode": "PercentileOffer",
          "position": 2,
          "percentile": 15,
          "adjustmentAmount": -0.01,
          "adjustmentPercent": 0,
          "canIncrease": true,
          "canDecrease": true,
          "priority": 0,
          "isActive": true
        },
        {
          "id": 11,
          "fromPrice": 1.01,
          "toPrice": 25,
          "referenceMode": "NthLowestOffer",
          "position": 3,
          "percentile": 20,
          "adjustmentAmount": 0,
          "adjustmentPercent": -2,
          "canIncrease": true,
          "canDecrease": true,
          "priority": 1,
          "isActive": true
        }
      ]
    }
    """;

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>Le stesse opzioni che usa MVC: default "web" più la configurazione dell'API.</summary>
    private static UpdateProfileRequest DeserializeComeLApi(string json)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJsonOptions.Configure(options);

        return JsonSerializer.Deserialize<UpdateProfileRequest>(json, options)!;
    }

    private static async Task<(AutoPricingController Controller, ApplicationDbContext Context, int ProfileId)> CreateControllerAsync()
    {
        var context = CreateContext();

        var profile = new PricingProfile
        {
            Name = "Profilo predefinito",
            IsActive = true,
            DryRun = true,
            Rules =
            {
                new PricingRule
                {
                    FromPrice = 0.02m, ToPrice = 1m,
                    ReferenceMode = PriceReferenceMode.PercentileOffer,
                    Position = 2, Percentile = 15m, AdjustmentAmount = -0.01m,
                    CanIncrease = true, CanDecrease = true, Priority = 0, IsActive = true
                }
            }
        };
        context.PricingProfiles.Add(profile);
        await context.SaveChangesAsync();

        var pricingService = new AutoPricingService(
            context,
            new Mock<ICardTraderApiService>().Object,
            new PricingEngine(),
            new ConfigurationBuilder().Build(),
            new Mock<ILogger<AutoPricingService>>().Object);

        var controller = new AutoPricingController(
            context, pricingService,
            new Mock<IPricingRunCoordinator>().Object,
            new Mock<ILogger<AutoPricingController>>().Object);

        return (controller, context, profile.Id);
    }

    [Fact]
    public async Task Il_profilo_si_salva_con_le_regole_come_le_manda_la_maschera()
    {
        var (controller, context, profileId) = await CreateControllerAsync();
        var request = DeserializeComeLApi(PayloadDallaMaschera);

        request.Rules.Should().NotBeNull("il binding delle regole è il passaggio che falliva");

        var result = await controller.UpdateProfile(profileId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        var rules = await context.PricingRules
            .AsNoTracking()
            .Where(r => r.PricingProfileId == profileId)
            .OrderBy(r => r.FromPrice)
            .ToListAsync();

        rules.Should().HaveCount(2, "le regole inviate sostituiscono integralmente quelle a database");
        rules[0].ReferenceMode.Should().Be(PriceReferenceMode.PercentileOffer);
        rules[0].Percentile.Should().Be(15m);
        rules[1].ReferenceMode.Should().Be(PriceReferenceMode.NthLowestOffer);
        rules[1].Position.Should().Be(3);
    }

    /// <summary>
    /// L'interruttore simulazione/attivo manda solo il campo che cambia: non deve azzerare
    /// le regole, che in quel corpo non compaiono affatto.
    /// </summary>
    [Fact]
    public async Task Cambiare_solo_la_modalita_non_tocca_le_regole()
    {
        var (controller, context, profileId) = await CreateControllerAsync();
        var request = DeserializeComeLApi("""{ "dryRun": false }""");

        await controller.UpdateProfile(profileId, request, CancellationToken.None);

        var profile = await context.PricingProfiles
            .AsNoTracking()
            .Include(p => p.Rules)
            .SingleAsync(p => p.Id == profileId);

        profile.DryRun.Should().BeFalse();
        profile.Rules.Should().HaveCount(1);
        profile.Name.Should().Be("Profilo predefinito", "i campi non inviati restano quelli di prima");
    }
}
