using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Persistence;

/// <summary>
/// Il registro delle valutazioni di prezzo deve sopravvivere alla carta a cui si riferisce.
/// Con la cancellazione a cascata la sincronizzazione notturna, rimuovendo le carte vendute e non
/// più presenti su Card Trader, si portava via anche il loro storico: spariva la traccia proprio
/// delle carte su cui conviene verificare se il prezzo proposto era corretto.
/// </summary>
public class PriceChangeLogDeleteBehaviorTests
{
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void InventoryItemRelationship_ShouldBeSetNull_NotCascade()
    {
        using var context = CreateContext();

        var foreignKey = context.Model
            .FindEntityType(typeof(PriceChangeLog))!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(InventoryItem));

        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.SetNull,
            "cancellare una carta venduta non deve cancellarne lo storico dei prezzi");
    }

    [Fact]
    public async Task DeletingInventoryItem_ShouldKeepPriceChangeLog_WithNullItemReference()
    {
        using var context = CreateContext();

        var item = new InventoryItem
        {
            BlueprintId = 42,
            CardTraderProductId = 999,
            Quantity = 1,
            ListingPrice = 19.99m,
            Condition = "Near Mint",
            Language = "English",
            Location = "Raccoglitore 1"
        };
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();

        context.PriceChangeLogs.Add(new PriceChangeLog
        {
            InventoryItemId = item.Id,
            BlueprintId = 42,
            OldPrice = 19.99m,
            ProposedPrice = 20.26m,
            Outcome = PricingOutcome.SimulatedDryRun,
            Reason = "verifica di regressione"
        });
        await context.SaveChangesAsync();

        // La carta viene venduta e sparisce dall'inventario alla sincronizzazione successiva.
        // I dipendenti vanno caricati perché il change tracker possa azzerarne il riferimento.
        await context.PriceChangeLogs.ToListAsync();
        context.InventoryItems.Remove(item);
        await context.SaveChangesAsync();

        var surviving = await context.PriceChangeLogs.SingleOrDefaultAsync();

        surviving.Should().NotBeNull("lo storico deve sopravvivere alla carta venduta");
        surviving!.InventoryItemId.Should().BeNull();
        surviving.BlueprintId.Should().Be(42, "la carta resta identificabile dal blueprint");
        surviving.ProposedPrice.Should().Be(20.26m);
    }
}
