using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Game> Games { get; set; }
    public DbSet<Expansion> Expansions { get; set; }
    public DbSet<Blueprint> Blueprints { get; set; }
    public DbSet<InventoryItem> InventoryItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Game -> Expansion (One-to-Many)
        modelBuilder.Entity<Game>()
            .HasMany(g => g.Expansions)
            .WithOne(e => e.Game)
            .HasForeignKey(e => e.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Expansion -> Blueprint (One-to-Many)
        modelBuilder.Entity<Expansion>()
            .HasMany(e => e.Blueprints)
            .WithOne(b => b.Expansion)
            .HasForeignKey(b => b.ExpansionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Blueprint -> InventoryItem (One-to-Many)
        modelBuilder.Entity<Blueprint>()
            .HasMany(b => b.InventoryItems)
            .WithOne(i => i.Blueprint)
            .HasForeignKey(i => i.BlueprintId)
            .OnDelete(DeleteBehavior.Cascade);

        // Order -> OrderItem (One-to-Many)
        modelBuilder.Entity<Order>()
            .HasMany(o => o.OrderItems)
            .WithOne(oi => oi.Order)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // InventoryItem -> OrderItem (One-to-Many)
        modelBuilder.Entity<InventoryItem>()
            .HasMany(i => i.OrderItems)
            .WithOne(oi => oi.InventoryItem)
            .HasForeignKey(oi => oi.InventoryItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure decimal precision for prices
        modelBuilder.Entity<InventoryItem>()
            .Property(i => i.PurchasePrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<InventoryItem>()
            .Property(i => i.ListingPrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingCost)
            .HasPrecision(18, 2);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.PricePerItem)
            .HasPrecision(18, 2);
    }
}
