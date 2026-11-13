using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Handlers;

/// <summary>
/// Il webhook non è raggiungibile: le vendite arrivano leggendo gli ordini. Un ordine nuovo deve
/// passare dal percorso del webhook (giacenza e riprezzo), uno già noto o storico no.
/// </summary>
public class NewOrderIntakeServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private static CardTraderOrderDto Order(int id, string state, string buyer, DateTime? paidAt = null) => new()
    {
        Id = id,
        Code = $"ordine-{id}",
        State = state,
        Buyer = new CardTraderUserDto { Id = 1, Username = buyer },
        PaidAt = paidAt,
        OrderItems = new List<CardTraderOrderItemDto>()
    };

    [Fact]
    public void Un_ordine_hub_pending_e_una_vendita_nuova()
        => NewOrderIntakeService.IsFreshSale(Order(1, "hub_pending", "Ct connect"), Now).Should().BeTrue();

    [Fact]
    public void L_ordine_raccolto_di_Card_Trader_Zero_non_e_una_vendita_nuova()
        => NewOrderIntakeService.IsFreshSale(Order(1, "paid", "Ct connect", Now.AddHours(-1)), Now)
            .Should().BeFalse("le sue righe sono già arrivate una per una come hub_pending");

    [Fact]
    public void Un_ordine_pagato_oggi_e_una_vendita_nuova()
        => NewOrderIntakeService.IsFreshSale(Order(1, "paid", "mario", Now.AddHours(-3)), Now).Should().BeTrue();

    [Fact]
    public void Un_ordine_pagato_settimane_fa_e_storico()
        => NewOrderIntakeService.IsFreshSale(Order(1, "sent", "mario", Now.AddDays(-20)), Now)
            .Should().BeFalse("sincronizzare un intervallo vecchio non deve scalare giacenze già allineate");

    [Fact]
    public async Task Solo_gli_ordini_mai_visti_passano_dal_percorso_del_webhook()
    {
        var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        context.Orders.Add(new Order { CardTraderOrderId = 10, Code = "ordine-10", State = "hub_pending" });
        await context.SaveChangesAsync();

        var mediator = new Mock<IMediator>();
        var mapper = new CardTraderDtoMapper(new Mock<ILogger<CardTraderDtoMapper>>().Object);
        var sync = new InventorySyncService(context, mapper, new Mock<ILogger<InventorySyncService>>().Object);
        var intake = new NewOrderIntakeService(context, mediator.Object, sync,
            new Mock<ILogger<NewOrderIntakeService>>().Object);

        var count = await intake.IngestAsync(new List<CardTraderOrderDto>
        {
            Order(10, "hub_pending", "Ct connect"),
            Order(11, "hub_pending", "Ct connect")
        });

        count.Should().Be(1);
        mediator.Verify(m => m.Send(
                It.Is<ProcessCardTraderWebhookCommand>(c => c.ObjectId == 11 && c.Cause == "order.create"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        mediator.Verify(m => m.Send(
                It.Is<ProcessCardTraderWebhookCommand>(c => c.ObjectId == 10),
                It.IsAny<CancellationToken>()),
            Times.Never, "un ordine già registrato non è una vendita nuova");
    }
}
