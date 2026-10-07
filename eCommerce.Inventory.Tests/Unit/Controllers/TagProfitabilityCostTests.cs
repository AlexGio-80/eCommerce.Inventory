using eCommerce.Inventory.Api.Controllers;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Controllers;

/// <summary>
/// Il report di redditività contava due volte il costo delle modifiche fatte dalla maschera, la cui
/// quantità è il nuovo totale dell'inserzione e non le copie aggiunte, e moltiplicava la giacenza
/// delle inserzioni modificate (un caricamento per modifica, tutti con lo stesso id Card Trader).
/// Caso di riferimento: Marvel Super Heroes, 3.674 € di costo invece di circa 2.040 €.
/// </summary>
public class TagProfitabilityCostTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly DateTime Opened = new(2026, 6, 26);

    /// <summary>
    /// Un'inserzione caricata con 100 copie a 0,30 €, poi modificata due volte portando il totale a 130
    /// e (dopo 10 vendite, registrate nello storico prezzi) a 150: copie aggiunte 100 + 30 + 30.
    /// Costo corretto 160 × 0,30 = 48 €; contando le modifiche per intero sarebbe 380 × 0,30 = 114 €.
    /// </summary>
    private async Task SeedAsync()
    {
        _db.Expansions.Add(new Expansion { Id = 1, Name = "Marvel Super Heroes", Code = "msh" });
        _db.Blueprints.Add(new Blueprint { Id = 1, ExpansionId = 1, Name = "Bulk", Version = "" });
        _db.PendingListings.AddRange(
            Upload(100, Opened, isUpdate: false),
            Upload(130, Opened.AddDays(20), isUpdate: true),
            Upload(150, Opened.AddDays(40), isUpdate: true));
        _db.PriceHistoryEntries.Add(new PriceHistoryEntry { BlueprintId = 1, CardTraderProductId = 555, Quantity = 120, Price = 0.05m, RecordedAt = Opened.AddDays(39) });
        _db.InventoryItems.Add(new InventoryItem { BlueprintId = 1, CardTraderProductId = 555, Quantity = 150, ListingPrice = 0.05m, Condition = "NM", Language = "English", Location = "" });
        _db.Orders.Add(new Order { Id = 1, Code = "A", PaidAt = Opened.AddDays(30) });
        _db.OrderItems.Add(new OrderItem { OrderId = 1, BlueprintId = 1, Quantity = 10, Price = 0.50m, Name = "Bulk", ExpansionName = "Marvel Super Heroes", Tag = "#MSH_OLD" });
        await _db.SaveChangesAsync();
    }

    private static PendingListing Upload(int quantity, DateTime createdAt, bool isUpdate) => new()
    {
        BlueprintId = 1, Quantity = quantity, PurchasePrice = 0.30m, SellingPrice = 0.05m, Tag = "#MSH_OLD",
        CreatedAt = createdAt, IsUpdate = isUpdate, CardTraderProductId = 555, Condition = "NM", Language = "English", IsSynced = true
    };

    private ReportingController CreateController() =>
        new(_db, NullLogger<ReportingController>.Instance, new PurchaseCostService(_db));

    [Fact]
    public async Task PurchaseCost_CountsOnlyTheCopiesAddedByUpdates()
    {
        await SeedAsync();
        var service = new PurchaseCostService(_db);

        (await service.CostByExpansionAsync())["Marvel Super Heroes"].Should().Be(48m);
        (await service.CostByTagAsync())["#MSH_OLD"].Should().Be(48m);
        (await service.GetRowsAsync()).Select(r => r.AddedCopies).Should().BeEquivalentTo(new[] { 100, 30, 30 });
    }

    [Fact]
    public async Task ListingCreatedOutsideTheForm_ItsFirstUpdateCountsInFull()
    {
        // Inserzione nata direttamente su Card Trader con 40 copie: lo storico le conosce, ma il loro
        // costo entra solo con la prima modifica dalla maschera, che quindi conta per intero.
        _db.Expansions.Add(new Expansion { Id = 2, Name = "Shadows over Innistrad", Code = "soi" });
        _db.Blueprints.Add(new Blueprint { Id = 2, ExpansionId = 2, Name = "Carta", Version = "" });
        _db.PriceHistoryEntries.Add(new PriceHistoryEntry { BlueprintId = 2, CardTraderProductId = 777, Quantity = 40, Price = 0.10m, RecordedAt = Opened });
        _db.PendingListings.AddRange(
            new PendingListing { BlueprintId = 2, Quantity = 40, PurchasePrice = 0.50m, SellingPrice = 0.10m, Tag = "#SOI_OLD", CreatedAt = Opened.AddDays(5), IsUpdate = true, CardTraderProductId = 777, Condition = "NM", Language = "English" },
            new PendingListing { BlueprintId = 2, Quantity = 40, PurchasePrice = 0.50m, SellingPrice = 0.20m, Tag = "#SOI_OLD", CreatedAt = Opened.AddDays(9), IsUpdate = true, CardTraderProductId = 777, Condition = "NM", Language = "English" });
        await _db.SaveChangesAsync();

        // L'espansione si chiama "Shadows Over Innistrad", gli ordini riportano "Shadows over Innistrad"
        _db.Expansions.Find(2)!.Name = "Shadows Over Innistrad";
        await _db.SaveChangesAsync();
        var cost = (await new PurchaseCostService(_db).CostByExpansionAsync())["Shadows over Innistrad"];

        cost.Should().Be(20m, "40 copie a 0,50 € una volta sola: la seconda modifica cambia solo il prezzo");
    }

    [Fact]
    public async Task TagProfitability_UsesTheAddedCopies_AndCountsTheStockOncePerListing()
    {
        await SeedAsync();

        var response = await CreateController().GetTagProfitability();

        var tag = ((response.Result as OkObjectResult)!.Value as Api.Models.ApiResponse<List<eCommerce.Inventory.Api.Models.Reporting.TagProfitabilityDto>>)!.Data!.Single();
        tag.TotaleAcquistato.Should().Be(48m, "non 114 €: le modifiche contano solo per le copie aggiunte");
        tag.TotaleVenduto.Should().Be(5m);
        tag.Differenza.Should().Be(-43m);
        tag.QtaRimanente.Should().Be(150, "l'inserzione ha tre caricamenti, ma la giacenza è una sola");
    }

    [Fact]
    public async Task TagExpansionProfitability_UsesTheAddedCopies()
    {
        await SeedAsync();

        var response = await CreateController().GetTagExpansionProfitability("#MSH_OLD");

        var row = ((response.Result as OkObjectResult)!.Value as Api.Models.ApiResponse<List<eCommerce.Inventory.Api.Models.Reporting.TagExpansionProfitabilityDto>>)!.Data!.Single();
        row.TotaleAcquistato.Should().Be(48m);
        row.QtaRimanente.Should().Be(150);
    }
}
