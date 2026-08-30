using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Handlers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Handlers;

/// <summary>
/// La giacenza locale viene scritta dall'export solo durante la sincronizzazione notturna.
/// Senza scalarla all'arrivo del webhook, per tutta la giornata l'inventario mostrerebbe carte
/// già vendute, e la rivalutazione immediata spenderebbe chiamate al marketplace — risorsa
/// limitata a 20 al minuto — su carte che non ci sono più.
/// </summary>
public class RepriceOnSaleStockTests
{
    private const int BlueprintCtId = 7001;
    private const int ProductCtId = 500;

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(ApplicationDbContext Context, int BlueprintId)> SeedAsync(int quantity)
    {
        var context = CreateContext();

        var blueprint = new Blueprint
        {
            CardTraderId = BlueprintCtId,
            Name = "Carta di prova",
            Version = "Regular",
            Rarity = "Rare"
        };
        context.Blueprints.Add(blueprint);
        await context.SaveChangesAsync();

        context.InventoryItems.Add(new InventoryItem
        {
            BlueprintId = blueprint.Id,
            CardTraderProductId = ProductCtId,
            Quantity = quantity,
            ListingPrice = 10m,
            Condition = "Near Mint",
            Language = "English",
            Location = "Raccoglitore 1"
        });
        await context.SaveChangesAsync();

        return (context, blueprint.Id);
    }

    private static (ProcessCardTraderWebhookHandler Handler, Mock<IPriceRefreshQueue> Queue) CreateHandler(
        ApplicationDbContext context, bool repriceOnOrder = true)
    {
        var queue = new Mock<IPriceRefreshQueue>();
        var mapper = new CardTraderDtoMapper(new Mock<ILogger<CardTraderDtoMapper>>().Object);
        var syncService = new InventorySyncService(context, mapper, new Mock<ILogger<InventorySyncService>>().Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AutoPricing:RepriceOnOrder"] = repriceOnOrder.ToString()
            })
            .Build();

        var handler = new ProcessCardTraderWebhookHandler(
            context,
            syncService,
            new Mock<INotificationService>().Object,
            queue.Object,
            configuration,
            new Mock<ILogger<ProcessCardTraderWebhookHandler>>().Object);

        return (handler, queue);
    }

    private static ProcessCardTraderWebhookCommand OrderWebhook(int orderId, int soldQuantity) =>
        new(
            webhookId: $"wh-{orderId}",
            cause: "order.create",
            objectId: orderId,
            mode: "live",
            data: new CardTraderOrderDto
            {
                Id = orderId,
                Code = $"ordine-{orderId}",
                OrderItems = new List<CardTraderOrderItemDto>
                {
                    new()
                    {
                        ProductId = ProductCtId,
                        BlueprintId = BlueprintCtId,
                        Quantity = soldQuantity,
                        Name = "Carta di prova"
                    }
                }
            });

    [Fact]
    public async Task Vendere_una_copia_su_dieci_scala_la_giacenza_e_riprezza_le_altre()
    {
        var (context, blueprintId) = await SeedAsync(quantity: 10);
        var (handler, queue) = CreateHandler(context);

        await handler.Handle(OrderWebhook(orderId: 1, soldQuantity: 1), CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.Quantity.Should().Be(9, "la vendita dev'essere visibile subito, non alla sincronizzazione notturna");

        queue.Verify(q => q.Enqueue(blueprintId, It.IsAny<string>(), PricingTrigger.OrderReceived), Times.Once,
            "restano nove copie da rivalutare: è il caso per cui il meccanismo esiste");
    }

    /// <summary>
    /// La stessa coda serve anche le nuove inserzioni, e il worker che la consuma gira sempre:
    /// l'interruttore del reprice alla vendita deve quindi fermare l'accodamento, non il consumo.
    /// </summary>
    [Fact]
    public async Task Con_il_reprice_alla_vendita_spento_non_viene_accodato_nulla()
    {
        var (context, _) = await SeedAsync(quantity: 10);
        var (handler, queue) = CreateHandler(context, repriceOnOrder: false);

        await handler.Handle(OrderWebhook(orderId: 5, soldQuantity: 1), CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.Quantity.Should().Be(9, "la giacenza va scalata comunque: non dipende dall'autopricer");

        queue.Verify(q => q.Enqueue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<PricingTrigger>()), Times.Never);
    }

    [Fact]
    public async Task Vendere_l_ultima_copia_azzera_la_giacenza_e_non_riprezza()
    {
        var (context, _) = await SeedAsync(quantity: 1);
        var (handler, queue) = CreateHandler(context);

        await handler.Handle(OrderWebhook(orderId: 2, soldQuantity: 1), CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.Quantity.Should().Be(0);

        queue.Verify(q => q.Enqueue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<PricingTrigger>()), Times.Never,
            "non c'è più nulla da riprezzare: la chiamata al marketplace sarebbe sprecata");
    }

    /// <summary>
    /// Card Trader può recapitare lo stesso webhook più di una volta. Scalare due volte la
    /// stessa vendita porterebbe la giacenza sotto il valore reale, e per una carta con una
    /// sola copia significherebbe farla sparire dall'inventario fino alla notte successiva.
    /// </summary>
    [Fact]
    public async Task Un_webhook_recapitato_due_volte_non_scala_due_volte()
    {
        var (context, _) = await SeedAsync(quantity: 10);
        var (handler, _) = CreateHandler(context);

        var webhook = OrderWebhook(orderId: 3, soldQuantity: 2);
        await handler.Handle(webhook, CancellationToken.None);
        await handler.Handle(webhook, CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.Quantity.Should().Be(8, "la seconda consegna dello stesso ordine non deve produrre effetti");
    }

    [Fact]
    public async Task La_giacenza_non_va_mai_sotto_zero()
    {
        // Se il dato locale era già disallineato, portarlo in negativo aggiungerebbe un errore
        // al posto di limitarlo. La sincronizzazione notturna riscrive comunque il valore vero.
        var (context, _) = await SeedAsync(quantity: 1);
        var (handler, _) = CreateHandler(context);

        await handler.Handle(OrderWebhook(orderId: 4, soldQuantity: 3), CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.Quantity.Should().Be(0);
    }
}
