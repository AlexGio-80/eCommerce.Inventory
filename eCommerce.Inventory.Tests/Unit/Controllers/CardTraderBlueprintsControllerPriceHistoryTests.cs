using System.Text.Json;
using eCommerce.Inventory.Api.Controllers.CardTrader;
using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Controllers;

/// <summary>
/// Il grafico dello storico prezzi nella pagina "Nuovo Prodotto" ha bisogno di una serie per
/// inserzione (condizione/lingua/foil possono differire fra le copie della stessa carta), non
/// di un'unica lista di punti mescolati: è la parte di logica dell'endpoint che può sbagliare.
/// </summary>
public class CardTraderBlueprintsControllerPriceHistoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CardTraderBlueprintsController CreateController(ApplicationDbContext context) =>
        new(new Mock<IBlueprintRepository>().Object, context, NullLogger<CardTraderBlueprintsController>.Instance);

    [Fact]
    public async Task Raggruppa_i_punti_per_inserzione_non_per_carta()
    {
        var context = CreateContext();
        context.PriceHistoryEntries.AddRange(
            new PriceHistoryEntry
            {
                BlueprintId = 10, CardTraderProductId = 111, Condition = "Near Mint", Language = "English",
                IsFoil = false, Price = 5.00m, Quantity = 1, RecordedAt = new DateTime(2026, 8, 29)
            },
            new PriceHistoryEntry
            {
                BlueprintId = 10, CardTraderProductId = 111, Condition = "Near Mint", Language = "English",
                IsFoil = false, Price = 5.50m, Quantity = 1, RecordedAt = new DateTime(2026, 8, 30)
            },
            new PriceHistoryEntry
            {
                BlueprintId = 10, CardTraderProductId = 222, Condition = "Near Mint", Language = "English",
                IsFoil = true, Price = 12.00m, Quantity = 1, RecordedAt = new DateTime(2026, 8, 29)
            },
            new PriceHistoryEntry
            {
                // Blueprint diverso: non deve comparire nella risposta.
                BlueprintId = 99, CardTraderProductId = 333, Condition = "Near Mint", Language = "English",
                IsFoil = false, Price = 1.00m, Quantity = 1, RecordedAt = new DateTime(2026, 8, 29)
            });
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        var result = await controller.GetPriceHistory(10);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;

        // La forma esatta è un tipo anonimo: si passa dal JSON, come farebbe il client HTTP.
        // Stessa policy usata realmente dall'API (camelCase): senza, i nomi delle proprietà
        // del tipo anonimo restano PascalCase e le asserzioni sotto non troverebbero nulla.
        var json = JsonSerializer.Serialize(response.Data, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var series = doc.RootElement.EnumerateArray().ToList();

        series.Should().HaveCount(2, "due CardTraderProductId distinti per il blueprint 10");

        var foilSeries = series.Single(s => s.GetProperty("isFoil").GetBoolean());
        foilSeries.GetProperty("cardTraderProductId").GetInt32().Should().Be(222);
        foilSeries.GetProperty("points").GetArrayLength().Should().Be(1);

        var nonFoilSeries = series.Single(s => !s.GetProperty("isFoil").GetBoolean());
        nonFoilSeries.GetProperty("cardTraderProductId").GetInt32().Should().Be(111);
        nonFoilSeries.GetProperty("points").GetArrayLength().Should().Be(2, "due rilevazioni di prezzo per la stessa inserzione");
    }

    [Fact]
    public async Task Senza_storico_restituisce_una_lista_vuota()
    {
        var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.GetPriceHistory(404);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;

        // Stessa policy usata realmente dall'API (camelCase): senza, i nomi delle proprietà
        // del tipo anonimo restano PascalCase e le asserzioni sotto non troverebbero nulla.
        var json = JsonSerializer.Serialize(response.Data, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }
}
