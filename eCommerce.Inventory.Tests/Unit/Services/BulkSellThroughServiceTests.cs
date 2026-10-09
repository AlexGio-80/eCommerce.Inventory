using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Quota venduta del bulk: si contano solo gli ordini pagati. Gli ordini "hub_pending" di Card Trader
/// Zero sono i singoli acquisti che Card Trader raccoglie poi in un ordine "Ct connect" pagato, con le
/// stesse righe: contarli farebbe vendere due volte.
/// </summary>
public class BulkSellThroughServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Gli_ordini_hub_pending_non_contano_come_vendite()
    {
        var release = new DateTime(2026, 6, 1);
        _db.Expansions.Add(new Expansion { Id = 1, Name = "Uscita", Code = "UXX", ReleaseDate = release });
        _db.Blueprints.Add(new Blueprint { Id = 1, ExpansionId = 1, Name = "Bulk", Version = "" });
        _db.InventoryItems.Add(new InventoryItem
        {
            BlueprintId = 1, Quantity = 300, ListingPrice = 0.05m, Condition = "Near Mint", Language = "English",
            Location = "", DateAdded = release
        });
        _db.Orders.Add(new Order { Id = 1, Code = "CONNECT", State = "done", PaidAt = release.AddDays(20) });
        _db.Orders.Add(new Order { Id = 2, Code = "HUB", State = "hub_pending", PaidAt = null });
        _db.OrderItems.Add(new OrderItem { OrderId = 1, BlueprintId = 1, Quantity = 200, Price = 0.05m, Name = "Bulk" });
        _db.OrderItems.Add(new OrderItem { OrderId = 2, BlueprintId = 1, Quantity = 200, Price = 0.05m, Name = "Bulk" });
        await _db.SaveChangesAsync();

        var result = await new BulkSellThroughService(_db).MeasureAsync(0.25m);

        result.Measured.Should().BeTrue();
        result.Expansions.Should().ContainSingle().Which.Sold.Should().Be(200, "le righe hub_pending sono un doppione");
        result.Share.Should().Be(0.4m, "200 vendute su 500 fra vendute e in vendita");
    }
}
