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
    public DbSet<User> Users { get; set; }
    public DbSet<ExpansionROI> ExpansionsROI { get; set; }
    public DbSet<PricingProfile> PricingProfiles { get; set; }
    public DbSet<PricingRule> PricingRules { get; set; }
    public DbSet<PriceChangeLog> PriceChangeLogs { get; set; }
    public DbSet<PricingRunLog> PricingRunLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAutoPricing(modelBuilder);

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

        modelBuilder.Entity<Expansion>()
            .Property(e => e.AverageCardValue)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Expansion>()
            .Property(e => e.TotalMinPrice)
            .HasPrecision(18, 2);

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

        // Configure decimal precision for prices
        modelBuilder.Entity<InventoryItem>()
            .Property(i => i.PurchasePrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<InventoryItem>()
            .Property(i => i.ListingPrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.SellerTotal)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.SellerFee)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.SellerSubtotal)
            .HasPrecision(18, 2);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.Price)
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

        // Configure ExpansionROI as a keyless view
        modelBuilder.Entity<ExpansionROI>()
            .HasNoKey()
            .ToView("ExpansionsROI");
    }

    /// <summary>
    /// Configurazione delle entità dell'autopricer.
    /// </summary>
    private static void ConfigureAutoPricing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PricingProfile>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.CountryCodesCsv).HasMaxLength(500);
            entity.Property(p => p.MinPrice).HasPrecision(18, 2);
            entity.Property(p => p.MaxChangePercentPerRun).HasPrecision(9, 2);
            entity.Property(p => p.OutlierMadThreshold).HasPrecision(9, 4);

            entity.HasMany(p => p.Rules)
                .WithOne(r => r.PricingProfile)
                .HasForeignKey(r => r.PricingProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PricingRule>(entity =>
        {
            entity.Property(r => r.FromPrice).HasPrecision(18, 2);
            entity.Property(r => r.ToPrice).HasPrecision(18, 2);
            entity.Property(r => r.AdjustmentAmount).HasPrecision(18, 2);
            entity.Property(r => r.AdjustmentPercent).HasPrecision(9, 2);

            entity.HasIndex(r => new { r.PricingProfileId, r.FromPrice, r.ToPrice })
                .HasDatabaseName("IX_PricingRule_Profile_Range");
        });

        modelBuilder.Entity<PricingRunLog>(entity =>
        {
            entity.Property(r => r.TotalPriceDelta).HasPrecision(18, 2);
            entity.Property(r => r.ErrorMessage).HasMaxLength(2000);

            // CoveragePercent è calcolata in memoria dai contatori: non va persistita,
            // altrimenti si potrebbe disallineare dai valori da cui deriva.
            entity.Ignore(r => r.CoveragePercent);

            entity.HasOne(r => r.PricingProfile)
                .WithMany()
                .HasForeignKey(r => r.PricingProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => r.StartedAt).HasDatabaseName("IX_PricingRunLog_StartedAt");
        });

        modelBuilder.Entity<PriceChangeLog>(entity =>
        {
            entity.Property(c => c.OldPrice).HasPrecision(18, 2);
            entity.Property(c => c.ProposedPrice).HasPrecision(18, 2);
            entity.Property(c => c.ReferencePrice).HasPrecision(18, 2);
            entity.Property(c => c.Reason).HasMaxLength(1000);

            entity.HasOne(c => c.InventoryItem)
                .WithMany()
                .HasForeignKey(c => c.InventoryItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Blueprint)
                .WithMany()
                .HasForeignKey(c => c.BlueprintId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(c => c.PricingRunLog)
                .WithMany(r => r.Changes)
                .HasForeignKey(c => c.PricingRunLogId)
                .OnDelete(DeleteBehavior.SetNull);

            // Serve a rispondere in fretta a "quando è stata aggiornata l'ultima volta
            // questa carta?", che è la domanda alla base del problema di copertura.
            entity.HasIndex(c => new { c.BlueprintId, c.CreatedAt })
                .HasDatabaseName("IX_PriceChangeLog_Blueprint_CreatedAt");

            entity.HasIndex(c => c.CreatedAt).HasDatabaseName("IX_PriceChangeLog_CreatedAt");
        });
    }
}
