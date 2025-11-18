using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

/// <summary>
/// Service for syncing Card Trader data into local database
/// Handles INSERT/UPDATE/DELETE merge logic for products and orders
/// Following SPECIFICATIONS: Single Responsibility, Error Handling, Structured Logging
/// </summary>
public class InventorySyncService
{
    private readonly IApplicationDbContext _context;
    private readonly CardTraderDtoMapper _mapper;
    private readonly ILogger<InventorySyncService> _logger;

    public InventorySyncService(
        IApplicationDbContext context,
        CardTraderDtoMapper mapper,
        ILogger<InventorySyncService> logger)
    {
        _context = context;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Syncs games from Card Trader API into the database
    /// Handles INSERT/UPDATE for existing games
    /// </summary>
    public async Task SyncGamesAsync(List<CardTraderGameDto> gameDtos, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!gameDtos.Any())
            {
                _logger.LogInformation("No games to sync");
                return;
            }

            _logger.LogInformation("Starting sync for {GameCount} games", gameDtos.Count);

            var dbContext = _context as DbContext;
            var existingGames = await dbContext!.Set<Game>()
                .AsNoTracking()
                .Where(g => gameDtos.Select(d => d.Id).Contains(g.CardTraderId))
                .ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0;

            foreach (var dto in gameDtos)
            {
                var existingGame = existingGames.FirstOrDefault(g => g.CardTraderId == dto.Id);

                if (existingGame == null)
                {
                    // INSERT: New game
                    var newGame = _mapper.MapGame(dto);
                    dbContext!.Set<Game>().Add(newGame);
                    insertCount++;
                }
                else
                {
                    // UPDATE: Existing game
                    existingGame.Name = dto.Name;
                    existingGame.Code = dto.DisplayName;
                    dbContext!.Set<Game>().Update(existingGame);
                    updateCount++;
                }
            }

            await dbContext!.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Games synced: {InsertCount} inserted, {UpdateCount} updated",
                insertCount, updateCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing games from Card Trader");
            throw;
        }
    }

    /// <summary>
    /// Syncs expansions from Card Trader API into the database
    /// Handles INSERT/UPDATE for existing expansions
    /// </summary>
    public async Task SyncExpansionsAsync(List<CardTraderExpansionDto> expansionDtos, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!expansionDtos.Any())
            {
                _logger.LogInformation("No expansions to sync");
                return;
            }

            _logger.LogInformation("Starting sync for {ExpansionCount} expansions", expansionDtos.Count);

            var dbContext = _context as DbContext;
            var existingExpansions = await dbContext!.Set<Expansion>()
                .AsNoTracking()
                .Where(e => expansionDtos.Select(d => d.Id).Contains(e.CardTraderId))
                .ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0;

            foreach (var dto in expansionDtos)
            {
                var existingExpansion = existingExpansions.FirstOrDefault(e => e.CardTraderId == dto.Id);

                if (existingExpansion == null)
                {
                    // INSERT: New expansion
                    var newExpansion = _mapper.MapExpansion(dto);
                    dbContext!.Set<Expansion>().Add(newExpansion);
                    insertCount++;
                }
                else
                {
                    // UPDATE: Existing expansion
                    existingExpansion.Name = dto.Name;
                    existingExpansion.Code = dto.Abbreviation;
                    existingExpansion.GameId = dto.GameId;
                    dbContext!.Set<Expansion>().Update(existingExpansion);
                    updateCount++;
                }
            }

            await dbContext!.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Expansions synced: {InsertCount} inserted, {UpdateCount} updated",
                insertCount, updateCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing expansions from Card Trader");
            throw;
        }
    }

    /// <summary>
    /// Syncs blueprints from Card Trader API into the database
    /// Handles INSERT/UPDATE for existing blueprints
    /// </summary>
    public async Task SyncBlueprintsAsync(List<CardTraderBlueprintDto> blueprintDtos, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!blueprintDtos.Any())
            {
                _logger.LogInformation("No blueprints to sync");
                return;
            }

            _logger.LogInformation("Starting sync for {BlueprintCount} blueprints", blueprintDtos.Count);

            var dbContext = _context as DbContext;
            var existingBlueprints = await dbContext!.Set<Blueprint>()
                .AsNoTracking()
                .Where(b => blueprintDtos.Select(d => d.Id).Contains(b.CardTraderId))
                .ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0;

            foreach (var dto in blueprintDtos)
            {
                var existingBlueprint = existingBlueprints.FirstOrDefault(b => b.CardTraderId == dto.Id);

                if (existingBlueprint == null)
                {
                    // INSERT: New blueprint
                    var newBlueprint = _mapper.MapBlueprint(dto);
                    dbContext!.Set<Blueprint>().Add(newBlueprint);
                    insertCount++;
                }
                else
                {
                    // UPDATE: Existing blueprint
                    existingBlueprint.Name = dto.Name;
                    existingBlueprint.Rarity = dto.Rarity;
                    existingBlueprint.ExpansionId = dto.ExpansionId;
                    dbContext!.Set<Blueprint>().Update(existingBlueprint);
                    updateCount++;
                }
            }

            await dbContext!.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Blueprints synced: {InsertCount} inserted, {UpdateCount} updated",
                insertCount, updateCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing blueprints from Card Trader");
            throw;
        }
    }

    /// <summary>
    /// Syncs categories from Card Trader API into the database
    /// Handles INSERT/UPDATE for categories and their properties/values
    /// </summary>
    public async Task SyncCategoriesAsync(List<CardTraderCategoryDto> categoryDtos, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!categoryDtos.Any())
            {
                _logger.LogInformation("No categories to sync");
                return;
            }

            _logger.LogInformation("Starting sync for {CategoryCount} categories", categoryDtos.Count);

            var dbContext = _context as DbContext;

            // Load all games to map CardTraderId -> Id
            var allGames = await dbContext!.Set<Game>()
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var existingCategories = await dbContext!.Set<Category>()
                .AsNoTracking()
                .Include(c => c.Properties)
                .ThenInclude(p => p.PossibleValues)
                .Where(c => categoryDtos.Select(d => d.Id).Contains(c.CardTraderId))
                .ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0, skippedCount = 0;

            foreach (var dto in categoryDtos)
            {
                // Find the corresponding Game by CardTraderId
                var gameEntity = allGames.FirstOrDefault(g => g.CardTraderId == dto.GameId);
                if (gameEntity == null)
                {
                    _logger.LogWarning("Category {CategoryId} references Game {GameId} which doesn't exist in database. Skipping.",
                        dto.Id, dto.GameId);
                    skippedCount++;
                    continue;
                }

                var existingCategory = existingCategories.FirstOrDefault(c => c.CardTraderId == dto.Id);

                if (existingCategory == null)
                {
                    // INSERT: New category with properties
                    var newCategory = new Category
                    {
                        CardTraderId = dto.Id,
                        Name = dto.Name,
                        GameId = gameEntity.Id, // Use the database Game Id
                        Properties = dto.Properties?.Select(p => new Property
                        {
                            Name = p.Name,
                            Type = p.Type,
                            PossibleValues = p.PossibleValues?
                                .Select(v => new PropertyValue { Value = v.ToString() ?? string.Empty })
                                .ToList() ?? new List<PropertyValue>()
                        }).ToList() ?? new List<Property>()
                    };

                    dbContext!.Set<Category>().Add(newCategory);
                    insertCount++;
                }
                else
                {
                    // UPDATE: Existing category
                    existingCategory.Name = dto.Name;
                    existingCategory.GameId = gameEntity.Id; // Use the database Game Id

                    // Update properties (simple approach: delete all and recreate)
                    var propertiesToDelete = existingCategory.Properties.ToList();
                    foreach (var prop in propertiesToDelete)
                    {
                        dbContext!.Set<Property>().Remove(prop);
                    }

                    // Add updated properties
                    if (dto.Properties != null && dto.Properties.Any())
                    {
                        var newProperties = dto.Properties.Select(p => new Property
                        {
                            Name = p.Name,
                            Type = p.Type,
                            PossibleValues = p.PossibleValues?
                                .Select(v => new PropertyValue { Value = v.ToString() ?? string.Empty })
                                .ToList() ?? new List<PropertyValue>()
                        }).ToList();

                        existingCategory.Properties = newProperties;
                    }

                    dbContext!.Set<Category>().Update(existingCategory);
                    updateCount++;
                }
            }

            await dbContext!.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Categories synced: {InsertCount} inserted, {UpdateCount} updated, {SkippedCount} skipped",
                insertCount, updateCount, skippedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing categories from Card Trader");
            throw;
        }
    }

    /// <summary>
    /// Syncs inventory products from Card Trader API into the database
    /// Handles INSERT/UPDATE/DELETE merge logic:
    /// - INSERT: New product not in DB
    /// - UPDATE: Product already exists, update fields
    /// - DELETE: Product in DB but not on marketplace (optional, logged as warning)
    /// </summary>
    public async Task SyncProductsAsync(List<CardTraderProductDto> productDtos, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!productDtos.Any())
            {
                _logger.LogInformation("No products to sync");
                return;
            }

            _logger.LogInformation("Starting sync for {ProductCount} products", productDtos.Count);

            var dbContext = _context as DbContext;
            var existingItems = await dbContext!.Set<InventoryItem>()
                .Where(i => i.CardTraderProductId.HasValue)
                .ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0, skippedCount = 0;

            foreach (var dto in productDtos)
            {
                var existingItem = existingItems.FirstOrDefault(i => i.CardTraderProductId == dto.Id);

                if (existingItem == null)
                {
                    // INSERT: New product
                    try
                    {
                        var newItem = _mapper.MapProductToInventoryItem(dto);
                        dbContext!.Set<InventoryItem>().Add(newItem);
                        insertCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to map product {ProductId} to inventory item", dto.Id);
                        skippedCount++;
                    }
                }
                else
                {
                    // UPDATE: Existing product
                    try
                    {
                        _mapper.UpdateInventoryItemFromProduct(existingItem, dto);
                        dbContext!.Set<InventoryItem>().Update(existingItem);
                        updateCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update inventory item {ItemId} from product {ProductId}",
                            existingItem.Id, dto.Id);
                        skippedCount++;
                    }
                }
            }

            // Optional: Log products in DB but not on marketplace (deleted from Card Trader)
            var deletedProductIds = existingItems
                .Select(i => i.CardTraderProductId)
                .Except(productDtos.Select(p => (int?)p.Id))
                .Where(id => id.HasValue)
                .ToList();

            if (deletedProductIds.Any())
            {
                _logger.LogWarning("Found {DeletedCount} products in DB not on Card Trader marketplace. " +
                    "These should be manually reviewed for deletion", deletedProductIds.Count);
            }

            await dbContext!.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Products synced: {InsertCount} inserted, {UpdateCount} updated, {SkippedCount} skipped",
                insertCount, updateCount, skippedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing products from Card Trader");
            throw;
        }
    }

    /// <summary>
    /// Syncs orders from Card Trader API into the database
    /// Handles INSERT/UPDATE for orders and order items
    /// </summary>
    public async Task SyncOrdersAsync(List<CardTraderOrderDto> orderDtos, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!orderDtos.Any())
            {
                _logger.LogInformation("No orders to sync");
                return;
            }

            _logger.LogInformation("Starting sync for {OrderCount} orders", orderDtos.Count);

            var dbContext = _context as DbContext;
            var existingOrders = await dbContext!.Set<Order>()
                .AsNoTracking()
                .Where(o => orderDtos.Select(d => d.Id).Contains(o.CardTraderOrderId))
                .ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0;

            foreach (var dto in orderDtos)
            {
                var existingOrder = existingOrders.FirstOrDefault(o => o.CardTraderOrderId == dto.Id);

                if (existingOrder == null)
                {
                    // INSERT: New order
                    var newOrder = new Order
                    {
                        CardTraderOrderId = dto.Id,
                        DatePlaced = DateTime.UtcNow,
                        Status = dto.State ?? "Pending",
                        TotalAmount = 0m, // Not provided in DTO
                        ShippingCost = 0m // Not provided in DTO
                    };
                    dbContext!.Set<Order>().Add(newOrder);
                    insertCount++;
                }
                else
                {
                    // UPDATE: Existing order status
                    existingOrder.Status = dto.State ?? existingOrder.Status;
                    dbContext!.Set<Order>().Update(existingOrder);
                    updateCount++;
                }
            }

            await dbContext!.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Orders synced: {InsertCount} inserted, {UpdateCount} updated",
                insertCount, updateCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing orders from Card Trader");
            throw;
        }
    }
}
