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
    public DbSet<PriceHistoryEntry> PriceHistoryEntries { get; set; }
    public DbSet<CardmarketProduct> CardmarketProducts { get; set; }
    public DbSet<CardmarketPriceSnapshot> CardmarketPriceSnapshots { get; set; }
    public DbSet<CardmarketImportLog> CardmarketImportLogs { get; set; }
    public DbSet<MtgjsonSet> MtgjsonSets { get; set; }
    public DbSet<SealedProduct> SealedProducts { get; set; }
    public DbSet<SealedProductContent> SealedProductContents { get; set; }
    public DbSet<MtgjsonCard> MtgjsonCards { get; set; }
    public DbSet<BoosterConfig> BoosterConfigs { get; set; }
    public DbSet<BoosterConfigSlot> BoosterConfigSlots { get; set; }
    public DbSet<BoosterSheet> BoosterSheets { get; set; }
    public DbSet<BoosterSheetCard> BoosterSheetCards { get; set; }
    public DbSet<MtgjsonDeck> MtgjsonDecks { get; set; }
    public DbSet<MtgjsonDeckCard> MtgjsonDeckCards { get; set; }
    public DbSet<CardmarketLatestPrice> CardmarketLatestPrices { get; set; }
    public DbSet<CardTraderCardPrice> CardTraderCardPrices { get; set; }
    public DbSet<ProductPurchase> ProductPurchases { get; set; }
    public DbSet<SealedOpportunity> SealedOpportunities { get; set; }
    public DbSet<AlertRule> AlertRules { get; set; }
    public DbSet<AlertRuleMatch> AlertRuleMatches { get; set; }
    public DbSet<AlertNotification> AlertNotifications { get; set; }
    public DbSet<SecretLairShopProduct> SecretLairShopProducts { get; set; }
    public DbSet<SecretLairShopCard> SecretLairShopCards { get; set; }
    public DbSet<SecretLairShopRun> SecretLairShopRuns { get; set; }

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
            entity.Property(p => p.MaxIncreasePercentPerRun).HasPrecision(9, 2);
            entity.Property(p => p.MaxDecreasePercentPerRun).HasPrecision(9, 2);
            entity.Property(p => p.GuardrailExemptAmount).HasPrecision(9, 2);
            entity.Property(p => p.MaxMedianRatio).HasPrecision(9, 2);
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

            // In cascata la sincronizzazione notturna, cancellando le carte vendute e non più
            // presenti su Card Trader, si porterebbe via anche il loro storico di valutazioni:
            // sparirebbe la traccia proprio delle carte su cui conviene verificare se il prezzo
            // proposto era corretto. Il registro deve sopravvivere alla carta.
            entity.HasOne(c => c.InventoryItem)
                .WithMany()
                .HasForeignKey(c => c.InventoryItemId)
                .OnDelete(DeleteBehavior.SetNull);

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

        modelBuilder.Entity<PriceHistoryEntry>(entity =>
        {
            entity.Property(h => h.Price).HasPrecision(18, 2);
            entity.Property(h => h.Condition).HasMaxLength(50);
            entity.Property(h => h.Language).HasMaxLength(50);

            entity.HasOne(h => h.Blueprint)
                .WithMany()
                .HasForeignKey(h => h.BlueprintId)
                .OnDelete(DeleteBehavior.NoAction);

            // Come per PriceChangeLog: la serie storica deve sopravvivere all'inserzione, o si
            // perderebbe l'andamento proprio delle carte vendute.
            entity.HasOne(h => h.InventoryItem)
                .WithMany()
                .HasForeignKey(h => h.InventoryItemId)
                .OnDelete(DeleteBehavior.SetNull);

            // La domanda tipica e' "come si e' mosso il prezzo di questa inserzione nel tempo":
            // l'indice la risolve senza scandire la tabella, che cresce a ogni sincronizzazione.
            entity.HasIndex(h => new { h.CardTraderProductId, h.RecordedAt })
                .HasDatabaseName("IX_PriceHistory_Product_RecordedAt");

            // Per i grafici aggregati sulla carta, quando interessano tutte le sue versioni.
            entity.HasIndex(h => new { h.BlueprintId, h.RecordedAt })
                .HasDatabaseName("IX_PriceHistory_Blueprint_RecordedAt");
        });

        modelBuilder.Entity<CardmarketProduct>(entity =>
        {
            // La chiave è l'id di Cardmarket: un id nostro non servirebbe a niente e costringerebbe
            // a una ricerca in più a ogni riga del listino.
            entity.HasKey(p => p.IdProduct);
            entity.Property(p => p.IdProduct).ValueGeneratedNever();
            entity.Property(p => p.Name).HasMaxLength(300);
            entity.Property(p => p.CategoryName).HasMaxLength(100);

            entity.HasIndex(p => p.IdExpansion).HasDatabaseName("IX_CardmarketProduct_IdExpansion");
        });

        modelBuilder.Entity<CardmarketPriceSnapshot>(entity =>
        {
            // Una riga per prodotto e giorno di listino: reimportare lo stesso listino non può
            // duplicare la serie.
            entity.HasKey(s => new { s.IdProduct, s.Date });

            foreach (var property in new[] { "Avg", "Low", "Trend", "Avg1", "Avg7", "Avg30", "AvgFoil", "LowFoil", "TrendFoil" })
            {
                entity.Property<decimal?>(property).HasPrecision(18, 2);
            }

            entity.HasIndex(s => s.Date).HasDatabaseName("IX_CardmarketPriceSnapshot_Date");
        });

        modelBuilder.Entity<SecretLairShopProduct>(entity =>
        {
            entity.HasIndex(p => p.WizardsProductId).IsUnique();
            entity.Property(p => p.WizardsProductId).HasMaxLength(50);
            entity.Property(p => p.RefId).HasMaxLength(50);
            entity.Property(p => p.Title).HasMaxLength(300);
            entity.Property(p => p.DropName).HasMaxLength(300);
            entity.Property(p => p.Price).HasPrecision(9, 2);
            entity.Property(p => p.EstimatedNetValue).HasPrecision(9, 2);
            entity.HasMany(p => p.Cards).WithOne(c => c.Product!).HasForeignKey(c => c.SecretLairShopProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecretLairShopCard>(entity =>
        {
            entity.Property(c => c.CardName).HasMaxLength(200);
            entity.Property(c => c.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<SecretLairShopRun>(entity =>
        {
            entity.Property(r => r.Message).HasMaxLength(2000);
            entity.HasIndex(r => r.StartedAt);
        });

        modelBuilder.Entity<CardmarketImportLog>(entity =>
        {
            entity.Property(l => l.Message).HasMaxLength(2000);
            entity.HasIndex(l => l.StartedAt).HasDatabaseName("IX_CardmarketImportLog_StartedAt");
        });

        modelBuilder.Entity<MtgjsonSet>(entity =>
        {
            entity.HasKey(s => s.Code);
            entity.Property(s => s.Code).HasMaxLength(20);
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.ParentCode).HasMaxLength(20);
            entity.Property(s => s.Type).HasMaxLength(50);

            entity.HasIndex(s => s.ParentCode).HasDatabaseName("IX_MtgjsonSet_ParentCode");
        });

        modelBuilder.Entity<SealedProduct>(entity =>
        {
            entity.Property(p => p.SetCode).HasMaxLength(20);
            entity.Property(p => p.Name).HasMaxLength(300);
            entity.Property(p => p.Category).HasMaxLength(50);
            entity.Property(p => p.Subtype).HasMaxLength(50);
            entity.Property(p => p.CtMinPrice).HasPrecision(18, 2);

            entity.HasIndex(p => p.Uuid).IsUnique().HasDatabaseName("IX_SealedProduct_Uuid");
            entity.HasIndex(p => p.SetCode).HasDatabaseName("IX_SealedProduct_SetCode");
            entity.HasIndex(p => p.CardmarketId).HasDatabaseName("IX_SealedProduct_CardmarketId");

            entity.HasMany(p => p.Contents)
                .WithOne(c => c.SealedProduct)
                .HasForeignKey(c => c.SealedProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SealedProductContent>(entity =>
        {
            entity.Property(c => c.Name).HasMaxLength(500);
            entity.Property(c => c.SetCode).HasMaxLength(20);
            entity.Property(c => c.PackCode).HasMaxLength(200);
        });

        modelBuilder.Entity<MtgjsonCard>(entity =>
        {
            entity.HasKey(c => c.Uuid);
            entity.Property(c => c.SetCode).HasMaxLength(20);
            entity.Property(c => c.Name).HasMaxLength(300);
            entity.Property(c => c.Number).HasMaxLength(20);
            entity.Property(c => c.Rarity).HasMaxLength(20);
            entity.Property(c => c.ScryfallId).HasMaxLength(50);
            entity.HasIndex(c => c.SetCode).HasDatabaseName("IX_MtgjsonCard_SetCode");
        });

        modelBuilder.Entity<BoosterConfig>(entity =>
        {
            entity.Property(c => c.SetCode).HasMaxLength(20);
            entity.Property(c => c.BoosterType).HasMaxLength(200);
            entity.HasIndex(c => new { c.SetCode, c.BoosterType }).HasDatabaseName("IX_BoosterConfig_Set_Type");
            entity.HasMany(c => c.Slots).WithOne(s => s.BoosterConfig).HasForeignKey(s => s.BoosterConfigId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BoosterConfigSlot>(entity =>
        {
            entity.Property(s => s.SheetName).HasMaxLength(200);
        });

        modelBuilder.Entity<BoosterSheet>(entity =>
        {
            entity.Property(s => s.SetCode).HasMaxLength(20);
            entity.Property(s => s.BoosterType).HasMaxLength(200);
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.HasIndex(s => new { s.SetCode, s.BoosterType }).HasDatabaseName("IX_BoosterSheet_Set_Type");
            entity.HasMany(s => s.Cards).WithOne(c => c.BoosterSheet).HasForeignKey(c => c.BoosterSheetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MtgjsonDeck>(entity =>
        {
            entity.Property(d => d.SetCode).HasMaxLength(20);
            entity.Property(d => d.Name).HasMaxLength(300);
            entity.HasIndex(d => d.SetCode).HasDatabaseName("IX_MtgjsonDeck_SetCode");
            entity.HasMany(d => d.Cards).WithOne(c => c.MtgjsonDeck).HasForeignKey(c => c.MtgjsonDeckId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardmarketLatestPrice>(entity =>
        {
            entity.HasKey(p => p.IdProduct);
            entity.Property(p => p.IdProduct).ValueGeneratedNever();
            foreach (var property in new[] { "Trend", "Low", "TrendFoil", "LowFoil" })
                entity.Property<decimal?>(property).HasPrecision(18, 2);
        });

        modelBuilder.Entity<CardTraderCardPrice>(entity =>
        {
            entity.HasKey(p => new { p.BlueprintId, p.IsFoil });
            entity.Property(p => p.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ProductPurchase>(entity =>
        {
            entity.Property(p => p.UnitPrice).HasPrecision(18, 2);
            entity.Property(p => p.PredictedOpenValueNet).HasPrecision(18, 2);
            entity.Property(p => p.PredictionCoverage).HasPrecision(5, 1);
            entity.Property(p => p.Store).HasMaxLength(100);
            entity.Property(p => p.Seller).HasMaxLength(100);
            entity.Property(p => p.Tag).HasMaxLength(100);
            entity.Property(p => p.Notes).HasMaxLength(1000);

            // Il catalogo sigillati non cancella mai i prodotti, ma un acquisto non deve comunque
            // poter sparire insieme a un prodotto: è un dato dell'utente, non di riferimento.
            entity.HasOne(p => p.SealedProduct)
                .WithMany()
                .HasForeignKey(p => p.SealedProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(p => p.Tag).HasDatabaseName("IX_ProductPurchase_Tag");
        });

        modelBuilder.Entity<SealedOpportunity>(entity =>
        {
            entity.Property(o => o.MainSetCode).HasMaxLength(20);
            entity.Property(o => o.Decision).HasMaxLength(30);
            foreach (var property in new[] { "CmTrend", "CmLow", "OpenValueCm", "SealedNetCm" })
                entity.Property<decimal?>(property).HasPrecision(18, 2);
            entity.Property(o => o.CoverageCm).HasPrecision(5, 1);
            entity.Property(o => o.OpeningRoiPercent).HasPrecision(9, 1);

            entity.HasOne(o => o.SealedProduct)
                .WithMany()
                .HasForeignKey(o => o.SealedProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(o => o.Date).HasDatabaseName("IX_SealedOpportunity_Date");
            entity.HasIndex(o => new { o.SealedProductId, o.Date }).HasDatabaseName("IX_SealedOpportunity_Product_Date");
        });

        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(200);
            entity.Property(r => r.SetCode).HasMaxLength(20);
            entity.Property(r => r.Category).HasMaxLength(50);
            entity.Property(r => r.Subtype).HasMaxLength(50);
            entity.Property(r => r.Threshold).HasPrecision(18, 2);

            entity.HasOne(r => r.SealedProduct)
                .WithMany()
                .HasForeignKey(r => r.SealedProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AlertRuleMatch>(entity =>
        {
            entity.HasOne(m => m.AlertRule)
                .WithMany()
                .HasForeignKey(m => m.AlertRuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(m => new { m.AlertRuleId, m.SealedProductId }).IsUnique()
                .HasDatabaseName("IX_AlertRuleMatch_Rule_Product");
        });

        modelBuilder.Entity<AlertNotification>(entity =>
        {
            entity.Property(n => n.SetCode).HasMaxLength(20);
            entity.Property(n => n.Title).HasMaxLength(300);
            entity.Property(n => n.Message).HasMaxLength(2000);
            entity.Property(n => n.EmailError).HasMaxLength(1000);

            // Un avviso già emesso resta anche se la regola viene cancellata.
            entity.HasOne(n => n.AlertRule)
                .WithMany()
                .HasForeignKey(n => n.AlertRuleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(n => n.CreatedAt).HasDatabaseName("IX_AlertNotification_CreatedAt");
            entity.HasIndex(n => n.ReadAt).HasDatabaseName("IX_AlertNotification_ReadAt");
        });
    }
}
