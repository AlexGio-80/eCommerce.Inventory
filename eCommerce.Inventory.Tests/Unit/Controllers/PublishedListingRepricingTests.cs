using eCommerce.Inventory.Api.Controllers;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Controllers;

/// <summary>
/// Una carta caricata dalla maschera entra in vendita con un prezzo messo a mano, spesso alto
/// di proposito. Perché l'autopricer possa riallinearlo senza aspettare la notte servono due
/// cose, e nessuna delle due è ovvia: la riga di magazzino dev'essere scritta subito — il
/// motore valuta gli InventoryItem, non le inserzioni in coda — e il blueprint dev'essere
/// accodato per la rivalutazione.
/// </summary>
public class PublishedListingRepricingTests
{
    private const int CardTraderProductId = 900;

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(ApplicationDbContext Context, int BlueprintId)> SeedPendingListingAsync()
    {
        var context = CreateContext();

        var blueprint = new Blueprint
        {
            CardTraderId = 4242,
            Name = "Carta appena caricata",
            Version = "Regular",
            Rarity = "Rare"
        };
        context.Blueprints.Add(blueprint);
        await context.SaveChangesAsync();

        context.PendingListings.Add(new PendingListing
        {
            BlueprintId = blueprint.Id,
            Quantity = 1,
            SellingPrice = 50m,
            PurchasePrice = 2m,
            Condition = "Near Mint",
            Language = "Italian",
            IsSynced = false
        });
        await context.SaveChangesAsync();

        return (context, blueprint.Id);
    }

    private static (PendingListingsController Controller, Mock<IPriceRefreshQueue> Queue) CreateController(
        ApplicationDbContext context, bool repriceOnListingSync = true)
    {
        var cardTrader = new Mock<ICardTraderApiService>();
        cardTrader
            .Setup(s => s.CreateProductOnCardTraderAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CardTraderProductId);

        var queue = new Mock<IPriceRefreshQueue>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AutoPricing:RepriceOnListingSync"] = repriceOnListingSync.ToString()
            })
            .Build();

        var controller = new PendingListingsController(
            context,
            cardTrader.Object,
            queue.Object,
            configuration,
            new Mock<ILogger<PendingListingsController>>().Object);

        return (controller, queue);
    }

    [Fact]
    public async Task La_pubblicazione_crea_subito_la_riga_di_magazzino_e_accoda_il_riprezzo()
    {
        var (context, blueprintId) = await SeedPendingListingAsync();
        var (controller, queue) = CreateController(context);

        await controller.SyncPendingListings(CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.CardTraderProductId.Should().Be(CardTraderProductId);
        item.ListingPrice.Should().Be(50m, "è il prezzo di partenza scelto a mano, sarà l'autopricer a correggerlo");
        item.Location.Should().NotBeNullOrEmpty("la colonna non ammette null e la maschera non raccoglie la collocazione");

        queue.Verify(q => q.Enqueue(blueprintId, It.IsAny<string>(), PricingTrigger.ListingCreated), Times.Once);
    }

    /// <summary>
    /// Card Trader può accorpare l'inserzione a un prodotto già esistente. Aggiungere comunque
    /// una riga lascerebbe due InventoryItem con lo stesso CardTraderProductId, e la
    /// sincronizzazione ne riconcilia uno solo: l'altro resterebbe fermo per sempre.
    /// </summary>
    [Fact]
    public async Task Se_Card_Trader_restituisce_un_prodotto_gia_noto_la_riga_viene_aggiornata_non_duplicata()
    {
        var (context, blueprintId) = await SeedPendingListingAsync();

        context.InventoryItems.Add(new InventoryItem
        {
            BlueprintId = blueprintId,
            CardTraderProductId = CardTraderProductId,
            Quantity = 3,
            ListingPrice = 12m,
            Condition = "Near Mint",
            Language = "Italian",
            Location = "Raccoglitore 1"
        });
        await context.SaveChangesAsync();

        var (controller, _) = CreateController(context);

        await controller.SyncPendingListings(CancellationToken.None);

        var item = await context.InventoryItems.SingleAsync();
        item.Quantity.Should().Be(1);
        item.ListingPrice.Should().Be(50m);
        item.Location.Should().Be("Raccoglitore 1", "la collocazione già nota non va persa");
    }

    [Fact]
    public async Task Con_il_riprezzo_delle_nuove_inserzioni_spento_la_riga_viene_creata_lo_stesso()
    {
        var (context, _) = await SeedPendingListingAsync();
        var (controller, queue) = CreateController(context, repriceOnListingSync: false);

        await controller.SyncPendingListings(CancellationToken.None);

        // La riga di magazzino non dipende dall'autopricer: descrive quello che c'è su Card Trader.
        (await context.InventoryItems.CountAsync()).Should().Be(1);

        queue.Verify(q => q.Enqueue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<PricingTrigger>()), Times.Never);
    }
}
