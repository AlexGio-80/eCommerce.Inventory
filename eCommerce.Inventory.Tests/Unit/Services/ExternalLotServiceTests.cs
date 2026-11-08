using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>Lotti di carte venduti fuori da Card Trader. Importi e siti inventati.</summary>
public class ExternalLotServiceTests
{
    [Fact]
    public void The_takings_are_split_to_the_cent_and_add_up_exactly()
    {
        var rows = ExternalLotService.Allocate(new List<(string, int)> { ("#AAA_OLD", 20), ("#BBB_OLD", 10) }, 7m);

        rows.Sum(r => r.Quantity * r.Price).Should().Be(7m);
        rows.Sum(r => r.Quantity).Should().Be(30);
        rows.Should().Contain(("#AAA_OLD", 10, 0.24m)).And.Contain(("#AAA_OLD", 10, 0.23m)).And.Contain(("#BBB_OLD", 10, 0.23m));
    }

    [Fact]
    public async Task A_lot_becomes_a_paid_order_with_the_expansion_of_the_tag_and_counts_without_fee()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Expansions.Add(new Expansion { Id = 1, Name = "Espansione Prova", Code = "aaa" });
        db.Orders.Add(new Order { CardTraderOrderId = 500, PaidAt = DateTime.Today, SellerSubtotal = 100m, SellerFee = 5m });
        await db.SaveChangesAsync();

        var order = await new ExternalLotService(db).RegisterAsync(
            new ExternalLotInput("Sito prova", new DateOnly(2026, 11, 8), 10m, new List<ExternalLotLine> { new("aaa_old", 100) }));

        order.CardTraderOrderId.Should().BeNegative();
        order.State.Should().Be(ExternalLotService.State);
        order.OrderItems.Should().ContainSingle().Which.Should().Match<OrderItem>(i =>
            i.Tag == "#AAA_OLD" && i.Quantity == 100 && i.Price == 0.10m && i.ExpansionName == "Espansione Prova" && i.IsPrepared);
        // La commissione si misura solo sugli ordini Card Trader.
        (await OpeningBalanceService.FeeShareAsync(db, CancellationToken.None)).Should().Be(0.05m);
        OpeningBalanceService.NetOf(new[] { (10m, true), (100m, false) }, 0.05m).Should().Be(105m);
    }
}
