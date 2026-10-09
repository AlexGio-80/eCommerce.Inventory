using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Card Trader Zero: le righe dell'ordine settimanale "Ct connect" ereditano la preparazione fatta giorno
/// per giorno sugli ordini "hub_pending", così in "Articoli da preparare" resta solo quello che manca.
/// </summary>
public class CtZeroPreparationReconcilerTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private int _nextItemId = 1;

    public void Dispose() => _db.Dispose();

    private Task<CtZeroReconcileResult> Reconcile() =>
        new CtZeroPreparationReconciler(_db, NullLogger.Instance).ReconcileAsync();

    /// <summary>Ordine "hub_pending" di una vendita: una riga per prodotto.</summary>
    private int Hub(int ctOrderId, int productId, int quantity = 1, bool prepared = false)
    {
        _db.Orders.Add(new Order { Id = ctOrderId, CardTraderOrderId = ctOrderId, Code = $"H{ctOrderId}", State = "hub_pending", BuyerUsername = "Ct connect" });
        var id = _nextItemId++;
        _db.OrderItems.Add(new OrderItem { Id = id, OrderId = ctOrderId, ProductId = productId, Quantity = quantity, IsPrepared = prepared, Name = $"P{productId}" });
        return id;
    }

    /// <summary>Ordine raccolto e pagato, con una riga per copia dei prodotti indicati.</summary>
    private List<int> Consolidated(int ctOrderId, DateTime paidAt, params int[] productIds)
    {
        _db.Orders.Add(new Order { Id = ctOrderId, CardTraderOrderId = ctOrderId, Code = $"C{ctOrderId}", State = "done", BuyerUsername = "Ct connect", PaidAt = paidAt });
        return productIds.Select(productId =>
        {
            var id = _nextItemId++;
            _db.OrderItems.Add(new OrderItem { Id = id, OrderId = ctOrderId, ProductId = productId, Quantity = 1, Name = $"P{productId}" });
            return id;
        }).ToList();
    }

    private async Task<OrderItem> Item(int id) => await _db.OrderItems.AsNoTracking().SingleAsync(i => i.Id == id);

    [Fact]
    public async Task Una_carta_gia_preparata_risulta_preparata_anche_nell_ordine_raccolto()
    {
        var hub = Hub(100, productId: 1, prepared: true);
        var rows = Consolidated(200, new DateTime(2026, 10, 4), 1);
        await _db.SaveChangesAsync();

        var result = await Reconcile();

        result.Should().Be(new CtZeroReconcileResult(1, 1, 0, 0));
        var row = await Item(rows[0]);
        row.IsPrepared.Should().BeTrue();
        row.HubOrderItemId.Should().Be(hub);
    }

    [Fact]
    public async Task Una_carta_non_ancora_preparata_resta_da_preparare_una_volta_sola()
    {
        var hub = Hub(100, productId: 1, prepared: false);
        var rows = Consolidated(200, new DateTime(2026, 10, 4), 1);
        await _db.SaveChangesAsync();

        await Reconcile();

        (await Item(rows[0])).IsPrepared.Should().BeFalse("si prepara sull'ordine raccolto");
        (await Item(hub)).IsPrepared.Should().BeTrue("la gemella hub_pending si chiude, altrimenti comparirebbe due volte");
    }

    [Fact]
    public async Task Le_copie_si_abbinano_una_per_una()
    {
        Hub(100, productId: 1, quantity: 3, prepared: true);
        var rows = Consolidated(200, new DateTime(2026, 10, 4), 1, 1, 1, 1);
        await _db.SaveChangesAsync();

        var result = await Reconcile();

        result.Linked.Should().Be(3);
        result.Unmatched.Should().Be(1, "la riga hub ha solo tre copie");
        (await Item(rows[3])).IsPrepared.Should().BeFalse("la quarta copia non è stata preparata: resta da recuperare");
    }

    [Fact]
    public async Task Una_carta_venduta_in_due_settimane_si_abbina_alla_vendita_giusta()
    {
        var firstWeek = Hub(100, productId: 1, prepared: true);
        var firstRows = Consolidated(200, new DateTime(2026, 9, 27), 1);
        var secondWeek = Hub(300, productId: 1, prepared: false);
        var secondRows = Consolidated(400, new DateTime(2026, 10, 4), 1);
        await _db.SaveChangesAsync();

        await Reconcile();

        (await Item(firstRows[0])).HubOrderItemId.Should().Be(firstWeek);
        var second = await Item(secondRows[0]);
        second.HubOrderItemId.Should().Be(secondWeek);
        second.IsPrepared.Should().BeFalse("la vendita della seconda settimana non era preparata");
    }

    [Fact]
    public async Task Un_ordine_hub_successivo_non_appartiene_all_ordine_raccolto()
    {
        var rows = Consolidated(200, new DateTime(2026, 10, 4), 1);
        Hub(300, productId: 1, prepared: true);
        await _db.SaveChangesAsync();

        var result = await Reconcile();

        result.Linked.Should().Be(0);
        (await Item(rows[0])).IsPrepared.Should().BeFalse();
    }

    [Fact]
    public async Task Un_secondo_giro_non_cambia_niente()
    {
        Hub(100, productId: 1, prepared: true);
        Hub(101, productId: 2, prepared: false);
        Consolidated(200, new DateTime(2026, 10, 4), 1, 2);
        await _db.SaveChangesAsync();
        await Reconcile();

        var again = await Reconcile();

        again.Should().Be(new CtZeroReconcileResult(0, 0, 0, 0));
    }

    [Fact]
    public async Task Gli_ordini_degli_altri_acquirenti_non_si_toccano()
    {
        Hub(100, productId: 1, prepared: true);
        _db.Orders.Add(new Order { Id = 200, CardTraderOrderId = 200, Code = "X", State = "done", BuyerUsername = "mario", PaidAt = new DateTime(2026, 10, 4) });
        _db.OrderItems.Add(new OrderItem { Id = 50, OrderId = 200, ProductId = 1, Quantity = 1, Name = "P1" });
        await _db.SaveChangesAsync();

        (await Reconcile()).Linked.Should().Be(0);
        (await Item(50)).IsPrepared.Should().BeFalse();
    }
}
