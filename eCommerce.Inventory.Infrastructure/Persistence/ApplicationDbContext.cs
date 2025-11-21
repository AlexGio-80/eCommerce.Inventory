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
    public DbSet<Category> Categories { get; set; }
    public DbSet<Property> Properties { get; set; }
    public DbSet<PropertyValue> PropertyValues { get; set; }
    public DbSet<InventoryItem> InventoryItems { get; set; }
    public DbSet<PendingListing> PendingListings { get; set; }
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

        // Game -> Category (One-to-Many)
        modelBuilder.Entity<Game>()
            .HasMany(g => g.Categories)
            .WithOne(c => c.Game)
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Category -> Property (One-to-Many)
        modelBuilder.Entity<Category>()
            .HasMany(c => c.Properties)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Property -> PropertyValue (One-to-Many)
        modelBuilder.Entity<Property>()
            .HasMany(p => p.PossibleValues)
            .WithOne(pv => pv.Property)
            .HasForeignKey(pv => pv.PropertyId)
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

        // Blueprint -> Game relationship (Many-to-One)
        modelBuilder.Entity<Blueprint>()
            .HasOne(b => b.Game)
            .WithMany()
            .HasForeignKey(b => b.GameId)
            .OnDelete(DeleteBehavior.NoAction);

        // Configure indices for Blueprint for optimal query performance
        modelBuilder.Entity<Blueprint>()
            .HasIndex(b => b.CardTraderId)
            .IsUnique()
            .HasDatabaseName("IX_Blueprint_CardTraderId");

        modelBuilder.Entity<Blueprint>()
            .HasIndex(b => b.GameId)
            .HasDatabaseName("IX_Blueprint_GameId");

        modelBuilder.Entity<Blueprint>()
            .HasIndex(b => b.ExpansionId)
            .HasDatabaseName("IX_Blueprint_ExpansionId");

        modelBuilder.Entity<Blueprint>()
            .HasIndex(b => b.Name)
            .HasDatabaseName("IX_Blueprint_Name");

        modelBuilder.Entity<Blueprint>()
            .HasIndex(b => new { b.GameId, b.ExpansionId })
            .HasDatabaseName("IX_Blueprint_GameId_ExpansionId");

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

        // PendingListing -> Blueprint (Many-to-One)
        modelBuilder.Entity<PendingListing>()
            .HasOne(pl => pl.Blueprint)
            .WithMany()
            .HasForeignKey(pl => pl.BlueprintId)
            .OnDelete(DeleteBehavior.Restrict);

        // PendingListing -> InventoryItem (Many-to-One, optional)
        modelBuilder.Entity<PendingListing>()
            .HasOne(pl => pl.InventoryItem)
            .WithMany()
            .HasForeignKey(pl => pl.InventoryItemId)
            .OnDelete(DeleteBehavior.SetNull);

        // Configure decimal precision for PendingListing prices
        modelBuilder.Entity<PendingListing>()
            .Property(pl => pl.SellingPrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PendingListing>()
            .Property(pl => pl.PurchasePrice)
            .HasPrecision(18, 2);

        // Index for pending listings queries
        modelBuilder.Entity<PendingListing>()
            .HasIndex(pl => pl.IsSynced)
            .HasDatabaseName("IX_PendingListing_IsSynced");

        modelBuilder.Entity<PendingListing>()
            .HasIndex(pl => pl.CreatedAt)
            .HasDatabaseName("IX_PendingListing_CreatedAt");
    }
}
