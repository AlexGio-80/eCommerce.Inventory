using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;

/// <summary>
/// Orchestrates the synchronization of Card Trader data with the local database
/// Handles fetching from API, mapping to domain entities, and upserting to database
/// </summary>
public class CardTraderSyncOrchestrator
{
    private readonly ICardTraderApiService _cardTraderApiService;
    private readonly CardTraderDtoMapper _dtoMapper;
    private readonly ApplicationDbContext _dbContext;
    private readonly InventorySyncService _inventorySyncService;
    private readonly ILogger<CardTraderSyncOrchestrator> _logger;

    public CardTraderSyncOrchestrator(
        ICardTraderApiService cardTraderApiService,
        CardTraderDtoMapper dtoMapper,
        ApplicationDbContext dbContext,
        InventorySyncService inventorySyncService,
        ILogger<CardTraderSyncOrchestrator> logger)
    {
        _cardTraderApiService = cardTraderApiService;
        _dtoMapper = dtoMapper;
        _dbContext = dbContext;
        _inventorySyncService = inventorySyncService;
        _logger = logger;
    }

    /// <summary>
    /// Performs selective synchronization based on request parameters
    /// </summary>
    public async Task<SyncResponseDto> SyncAsync(SyncRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = new SyncResponseDto
        {
            SyncStartTime = DateTime.UtcNow
        };

        try
        {
            // If nothing to sync, return empty response
            if (request.IsEmpty)
            {
                _logger.LogWarning("Sync request is empty - no entities selected for synchronization");
                response.ErrorMessage = "No entities selected for synchronization";
                response.SyncEndTime = DateTime.UtcNow;
                return response;
            }

            _logger.LogInformation("Starting selective sync. Games: {Games}, Categories: {Categories}, Expansions: {Expansions}, Blueprints: {Blueprints}, Properties: {Properties}",
                request.SyncGames, request.SyncCategories, request.SyncExpansions, request.SyncBlueprints, request.SyncProperties);

            // Sync Games
            if (request.SyncGames)
            {
                await SyncGamesAsync(response, cancellationToken);
            }

            // Sync Expansions (depends on Games being synced first)
            if (request.SyncExpansions)
            {
                await SyncExpansionsAsync(response, cancellationToken);
            }

            // Sync Blueprints (depends on Expansions)
            if (request.SyncBlueprints)
            {
                await SyncBlueprintsAsync(response, cancellationToken);
            }

            // Sync Categories (with nested Properties and PropertyValues)
            if (request.SyncCategories)
            {
                await SyncCategoriesAsync(response, cancellationToken);
            }

            // Properties sync currently handled as part of Categories (since they're nested)
            if (request.SyncProperties)
            {
                response.Properties.WasRequested = true;
                _logger.LogInformation("Properties are synced as part of Categories synchronization");
            }

            // Sync Inventory (Products)
            if (request.SyncInventory)
            {
                await SyncInventoryAsync(response, cancellationToken);
            }

            response.SyncEndTime = DateTime.UtcNow;
            _logger.LogInformation("Sync completed successfully. Added: {Added}, Updated: {Updated}, Failed: {Failed}, Skipped: {Skipped}, Duration: {Duration}ms",
                response.Added, response.Updated, response.Failed, response.Skipped, response.Duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = $"Sync failed: {ex.Message}";
            response.SyncEndTime = DateTime.UtcNow;
            _logger.LogError(ex, "Error during sync operation");
        }

        return response;
    }

    /// <summary>
    /// Syncs games from Card Trader API to database
    /// </summary>
    private async Task SyncGamesAsync(SyncResponseDto response, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Games sync");

            // Fetch games from API
            var gameDtos = await _cardTraderApiService.SyncGamesAsync(cancellationToken);
            var gameList = ConvertDynamicToList<CardTraderGameDto>(gameDtos);
            var games = _dtoMapper.MapGames(gameList);

            _logger.LogInformation("Fetched {GameCount} games from Card Trader API", games.Count);

            // Upsert games into database
            var result = await UpsertGamesAsync(games, cancellationToken);

            response.Games.WasRequested = true;
            response.Games.Added = result.Added;
            response.Games.Updated = result.Updated;
            response.Games.Failed = result.Failed;

            response.Added += result.Added;
            response.Updated += result.Updated;
            response.Failed += result.Failed;

            _logger.LogInformation("Games sync completed. Added: {Added}, Updated: {Updated}, Failed: {Failed}",
                result.Added, result.Updated, result.Failed);
        }
        catch (Exception ex)
        {
            response.Games.WasRequested = true;
            response.Games.ErrorMessage = ex.Message;
            response.Failed++;
            _logger.LogError(ex, "Error syncing games");
        }
    }

    /// <summary>
    /// Syncs expansions from Card Trader API to database
    /// </summary>
    private async Task SyncExpansionsAsync(SyncResponseDto response, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Expansions sync");

            // Fetch expansions from API
            var expansionDtos = await _cardTraderApiService.SyncExpansionsAsync(cancellationToken);
            var expansionList = ConvertDynamicToList<CardTraderExpansionDto>(expansionDtos);
            var expansions = _dtoMapper.MapExpansions(expansionList);

            _logger.LogInformation("Fetched {ExpansionCount} expansions from Card Trader API", expansions.Count);

            // Upsert expansions into database
            var result = await UpsertExpansionsAsync(expansions, cancellationToken);

            response.Expansions.WasRequested = true;
            response.Expansions.Added = result.Added;
            response.Expansions.Updated = result.Updated;
            response.Expansions.Failed = result.Failed;
            response.Expansions.Skipped = result.Skipped;

            response.Added += result.Added;
            response.Updated += result.Updated;
            response.Failed += result.Failed;
            response.Skipped += result.Skipped;

            _logger.LogInformation("Expansions sync completed. Added: {Added}, Updated: {Updated}, Failed: {Failed}, Skipped: {Skipped}",
                result.Added, result.Updated, result.Failed, result.Skipped);
        }
        catch (Exception ex)
        {
            response.Expansions.WasRequested = true;
            response.Expansions.ErrorMessage = ex.Message;
            response.Failed++;
            _logger.LogError(ex, "Error syncing expansions");
        }
    }

    /// <summary>
    /// Syncs blueprints for all expansions from Card Trader API to database
    /// </summary>
    private async Task SyncBlueprintsAsync(SyncResponseDto response, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Blueprints sync");

            // Get expansions only for enabled games
            var expansions = await _dbContext.Expansions
                .Include(e => e.Game)
                .Where(e => e.Game.IsEnabled)
                .ToListAsync(cancellationToken);
            _logger.LogInformation("Found {ExpansionCount} expansions for enabled games in database", expansions.Count);

            var totalAdded = 0;
            var totalUpdated = 0;
            var totalFailed = 0;

            // Sync blueprints for each expansion
            foreach (var expansion in expansions)
            {
                try
                {
                    var blueprintDtos = await _cardTraderApiService.SyncBlueprintsForExpansionAsync(expansion.CardTraderId, cancellationToken);
                    var blueprintList = ConvertDynamicToList<CardTraderBlueprintDto>(blueprintDtos);
                    var blueprints = _dtoMapper.MapBlueprints(blueprintList);

                    // Fix Foreign Keys: Map Card Trader IDs to Local Database IDs
                    foreach (var bp in blueprints)
                    {
                        bp.ExpansionId = expansion.Id;
                        bp.GameId = expansion.GameId;
                    }

                    var result = await UpsertBlueprintsAsync(blueprints, cancellationToken);
                    totalAdded += result.Added;
                    totalUpdated += result.Updated;
                    totalFailed += result.Failed;

                    _logger.LogInformation("Synced {BlueprintCount} blueprints for expansion {ExpansionId}", blueprints.Count, expansion.CardTraderId);
                }
                catch (Exception ex)
                {
                    totalFailed++;
                    _logger.LogError(ex, "Error syncing blueprints for expansion {ExpansionId}", expansion.CardTraderId);
                }
            }

            response.Blueprints.WasRequested = true;
            response.Blueprints.Added = totalAdded;
            response.Blueprints.Updated = totalUpdated;
            response.Blueprints.Failed = totalFailed;

            response.Added += totalAdded;
            response.Updated += totalUpdated;
            response.Failed += totalFailed;

            _logger.LogInformation("Blueprints sync completed. Added: {Added}, Updated: {Updated}, Failed: {Failed}",
                totalAdded, totalUpdated, totalFailed);
        }
        catch (Exception ex)
        {
            response.Blueprints.WasRequested = true;
            response.Blueprints.ErrorMessage = ex.Message;
            response.Failed++;
            _logger.LogError(ex, "Error syncing blueprints");
        }
    }

    /// <summary>
    /// Syncs categories (with nested properties and property values) from Card Trader API to database
    /// Only syncs categories for enabled games
    /// </summary>
    private async Task SyncCategoriesAsync(SyncResponseDto response, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Categories sync");

            // Load all enabled games for filtering
            var enabledGames = await _dbContext.Games
                .AsNoTracking()
                .Where(g => g.IsEnabled)
                .ToListAsync(cancellationToken);

            // Fetch categories from API
            var categoryDtos = await _cardTraderApiService.SyncCategoriesAsync(cancellationToken);
            var categoryList = ConvertDynamicToList<CardTraderCategoryDto>(categoryDtos);

            _logger.LogInformation("Fetched {CategoryCount} categories from Card Trader API", categoryList.Count);

            // Filter categories to only include those for enabled games
            var filteredCategories = categoryList
                .Where(c => enabledGames.Any(g => g.CardTraderId == c.GameId))
                .ToList();

            _logger.LogInformation("Filtered to {FilteredCategoryCount} categories for enabled games (skipped {SkippedCount})",
                filteredCategories.Count, categoryList.Count - filteredCategories.Count);

            // Use InventorySyncService to handle the full upsert logic with properties
            await _inventorySyncService.SyncCategoriesAsync(filteredCategories, cancellationToken);

            // Count the synchronized categories (we'll track as "Added" for now since InventorySyncService handles the detail)
            response.Categories.WasRequested = true;
            response.Categories.Added = categoryList.Count;

            response.Added += categoryList.Count;

            _logger.LogInformation("Categories sync completed. Synced: {CategoryCount}",
                categoryList.Count);
        }
        catch (Exception ex)
        {
            response.Categories.WasRequested = true;
            response.Categories.ErrorMessage = ex.Message;
            response.Failed++;
            _logger.LogError(ex, "Error syncing categories");
        }
    }

    /// <summary>
    /// Syncs inventory (products) from Card Trader API to database
    /// Uses export endpoint for full sync
    /// Deletes local items that are missing from API response (for enabled games)
    /// </summary>
    private async Task SyncInventoryAsync(SyncResponseDto response, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Inventory sync");

            // Load all enabled games for filtering
            var enabledGames = await _dbContext.Games
                .AsNoTracking()
                .Where(g => g.IsEnabled)
                .ToListAsync(cancellationToken);

            var enabledGameIds = enabledGames.Select(g => g.CardTraderId).ToHashSet();

            // Fetch products from API (Export endpoint)
            var productDtos = await _cardTraderApiService.GetProductsExportAsync(cancellationToken);
            var productList = ConvertDynamicToList<CardTraderProductDto>(productDtos);

            _logger.LogInformation("Fetched {ProductCount} products from Card Trader Export API", productList.Count);

            // Filter products to only include those for enabled games
            var filteredProducts = productList
                .Where(p => enabledGameIds.Contains(p.GameId))
                .ToList();

            var skippedCount = productList.Count - filteredProducts.Count;
            _logger.LogInformation("Filtered to {FilteredProductCount} products for enabled games (skipped {SkippedCount})",
                filteredProducts.Count, skippedCount);

            // Upsert products into database and handle deletions
            var result = await UpsertInventoryAsync(filteredProducts, enabledGameIds, cancellationToken);

            response.Inventory.WasRequested = true;
            response.Inventory.Added = result.Added;
            response.Inventory.Updated = result.Updated;
            response.Inventory.Failed = result.Failed;
            response.Inventory.Skipped = skippedCount; // Items for disabled games are skipped

            response.Added += result.Added;
            response.Updated += result.Updated;
            response.Failed += result.Failed;
            response.Skipped += skippedCount;

            _logger.LogInformation("Inventory sync completed. Added: {Added}, Updated: {Updated}, Failed: {Failed}, Skipped: {Skipped}, Deleted: {Deleted}",
                result.Added, result.Updated, result.Failed, skippedCount, result.Deleted);
        }
        catch (Exception ex)
        {
            response.Inventory.WasRequested = true;
            response.Inventory.ErrorMessage = ex.Message;
            response.Failed++;
            _logger.LogError(ex, "Error syncing inventory");
        }
    }

    /// <summary>
    /// Upserts inventory items and deletes missing ones
    /// </summary>
    private async Task<(int Added, int Updated, int Failed, int Deleted)> UpsertInventoryAsync(
        List<CardTraderProductDto> products,
        HashSet<int> enabledGameIds,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        var failed = 0;
        var deleted = 0;

        // 1. Get all existing inventory items for enabled games
        // We need to filter by Blueprint.Expansion.Game.CardTraderId
        // This is a bit complex query, so let's fetch items where Blueprint.GameId is in our enabled list
        // But Blueprint.GameId is local ID. We need to map enabledGameIds (CT IDs) to local IDs.

        var enabledGameLocalIds = await _dbContext.Games
            .Where(g => enabledGameIds.Contains(g.CardTraderId))
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        var existingItems = await _dbContext.InventoryItems
            .Include(i => i.Blueprint)
            .Where(i => i.Blueprint != null && enabledGameLocalIds.Contains(i.Blueprint.GameId))
            .ToListAsync(cancellationToken);

        var existingItemsMap = existingItems
            .Where(i => i.CardTraderProductId.HasValue)
            .ToDictionary(i => i.CardTraderProductId!.Value);

        var processedProductIds = new HashSet<int>();

        // 2. Upsert (Insert/Update)
        foreach (var product in products)
        {
            try
            {
                processedProductIds.Add(product.Id);

                if (existingItemsMap.TryGetValue(product.Id, out var existingItem))
                {
                    // Update
                    _dtoMapper.UpdateInventoryItemFromProduct(existingItem, product);
                    // Ensure BlueprintId matches (in case it changed, though unlikely for same product ID)
                    // existingItem.BlueprintId = product.BlueprintId; // Need to check if BlueprintId refers to CT ID or Local ID in DTO?
                    // DTO has 'blueprint_id' which is Card Trader Blueprint ID.
                    // InventoryItem.BlueprintId is LOCAL Blueprint ID.
                    // We need to find the local Blueprint ID for this CT Blueprint ID.

                    // Optimization: Only fetch blueprint if needed or batch fetch. 
                    // For now, let's assume BlueprintId link is correct if it exists.
                    // But if we are creating new, we definitely need to resolve it.

                    updated++;
                }
                else
                {
                    // Insert
                    // We need to find the local Blueprint ID
                    var blueprint = await _dbContext.Blueprints
                        .FirstOrDefaultAsync(b => b.CardTraderId == product.BlueprintId, cancellationToken);

                    if (blueprint == null)
                    {
                        _logger.LogWarning("Blueprint {BlueprintId} not found for Product {ProductId} (Expansion {ExpansionId}). Skipping.",
                            product.BlueprintId, product.Id, product.Expansion?.Id);
                        failed++;
                        continue;
                    }

                    var newItem = _dtoMapper.MapProductToInventoryItem(product);
                    newItem.BlueprintId = blueprint.Id; // Link to local Blueprint

                    _dbContext.InventoryItems.Add(newItem);
                    added++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Error upserting product {ProductId}", product.Id);
            }
        }

        // 3. Delete missing items
        // Items in existingItemsMap that are NOT in processedProductIds should be deleted
        foreach (var kvp in existingItemsMap)
        {
            if (!processedProductIds.Contains(kvp.Key))
            {
                try
                {
                    _dbContext.InventoryItems.Remove(kvp.Value);
                    deleted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Error deleting missing product {ProductId}", kvp.Key);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (added, updated, failed, deleted);
    }

    /// <summary>
    /// Upserts games into database (insert if not exists, update if exists)
    /// </summary>
    private async Task<(int Added, int Updated, int Failed)> UpsertGamesAsync(List<Game> games, CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        var failed = 0;

        foreach (var game in games)
        {
            try
            {
                var existingGame = await _dbContext.Games
                    .FirstOrDefaultAsync(g => g.CardTraderId == game.CardTraderId, cancellationToken);

                if (existingGame == null)
                {
                    _dbContext.Games.Add(game);
                    added++;
                }
                else
                {
                    existingGame.Name = game.Name;
                    existingGame.Code = game.Code;
                    _dbContext.Games.Update(existingGame);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Error upserting game {GameId}", game.CardTraderId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (added, updated, failed);
    }

    /// <summary>
    /// Upserts expansions into database (insert if not exists, update if exists)
    /// </summary>
    private async Task<(int Added, int Updated, int Failed, int Skipped)> UpsertExpansionsAsync(List<Expansion> expansions, CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        var failed = 0;
        var skipped = 0;

        // Load all games to map CardTraderId -> Database Id
        var allGames = await _dbContext.Games
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var expansion in expansions)
        {
            try
            {
                // Map the GameId from CardTraderId to database Game.Id
                var gameEntity = allGames.FirstOrDefault(g => g.CardTraderId == expansion.GameId);
                if (gameEntity == null)
                {
                    skipped++;
                    _logger.LogWarning("Expansion {ExpansionId} references Game {GameId} which doesn't exist in database. Skipping.",
                        expansion.CardTraderId, expansion.GameId);
                    continue;
                }

                // Skip expansions for disabled games
                if (!gameEntity.IsEnabled)
                {
                    skipped++;
                    _logger.LogDebug("Skipping expansion {ExpansionId} ({ExpansionName}) - Game {GameId} ({GameName}) is not enabled",
                        expansion.CardTraderId, expansion.Name, gameEntity.Id, gameEntity.Name);
                    continue;
                }

                var existingExpansion = await _dbContext.Expansions
                    .FirstOrDefaultAsync(e => e.CardTraderId == expansion.CardTraderId, cancellationToken);

                if (existingExpansion == null)
                {
                    // Set the correct GameId (database ID) before inserting
                    expansion.GameId = gameEntity.Id;
                    _dbContext.Expansions.Add(expansion);
                    added++;
                }
                else
                {
                    existingExpansion.Name = expansion.Name;
                    existingExpansion.Code = expansion.Code;
                    existingExpansion.GameId = gameEntity.Id; // Use the database Game Id
                    _dbContext.Expansions.Update(existingExpansion);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Error upserting expansion {ExpansionId}", expansion.CardTraderId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (added, updated, failed, skipped);
    }

    /// <summary>
    /// Upserts blueprints into database (insert if not exists, update if exists)
    /// </summary>
    private async Task<(int Added, int Updated, int Failed)> UpsertBlueprintsAsync(List<Blueprint> blueprints, CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        var failed = 0;

        foreach (var blueprint in blueprints)
        {
            try
            {
                var existingBlueprint = await _dbContext.Blueprints
                    .FirstOrDefaultAsync(b => b.CardTraderId == blueprint.CardTraderId, cancellationToken);

                if (existingBlueprint == null)
                {
                    _dbContext.Blueprints.Add(blueprint);
                    added++;
                }
                else
                {
                    existingBlueprint.Name = blueprint.Name;
                    existingBlueprint.Rarity = blueprint.Rarity;
                    existingBlueprint.Version = blueprint.Version;
                    existingBlueprint.ExpansionId = blueprint.ExpansionId;
                    _dbContext.Blueprints.Update(existingBlueprint);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Error upserting blueprint {BlueprintId}", blueprint.CardTraderId);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return (added, updated, failed);
    }

    /// <summary>
    /// Syncs blueprints for a specific expansion from Card Trader API to database
    /// </summary>
    public async Task<(int Added, int Updated, int Failed)> SyncBlueprintsForExpansionAsync(int expansionId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting Blueprints sync for expansion ID {ExpansionId}", expansionId);

            // Get the expansion
            var expansion = await _dbContext.Expansions
                .Include(e => e.Game)
                .FirstOrDefaultAsync(e => e.Id == expansionId, cancellationToken);

            if (expansion == null)
            {
                throw new ArgumentException($"Expansion with ID {expansionId} not found");
            }

            _logger.LogInformation("Syncing blueprints for expansion {ExpansionName} (CardTraderId: {CardTraderId})",
                expansion.Name, expansion.CardTraderId);

            // Fetch blueprints from Card Trader API
            var blueprintDtos = await _cardTraderApiService.SyncBlueprintsForExpansionAsync(expansion.CardTraderId, cancellationToken);
            var blueprintList = ConvertDynamicToList<CardTraderBlueprintDto>(blueprintDtos);
            var blueprints = _dtoMapper.MapBlueprints(blueprintList);

            _logger.LogInformation("Fetched {BlueprintCount} blueprints from Card Trader API", blueprints.Count);

            // Fix Foreign Keys: Map Card Trader IDs to Local Database IDs
            foreach (var bp in blueprints)
            {
                bp.ExpansionId = expansion.Id;
                bp.GameId = expansion.GameId;
            }

            // Upsert blueprints into database
            var result = await UpsertBlueprintsAsync(blueprints, cancellationToken);

            _logger.LogInformation("Blueprints sync for expansion {ExpansionId} completed. Added: {Added}, Updated: {Updated}, Failed: {Failed}",
                expansionId, result.Added, result.Updated, result.Failed);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing blueprints for expansion {ExpansionId}", expansionId);
            throw;
        }
    }

    /// <summary>
    /// Converts a collection of dynamic objects to a typed list using JSON serialization
    /// </summary>
    private List<T> ConvertDynamicToList<T>(IEnumerable<dynamic> items) where T : class
    {
        var result = new List<T>();

        foreach (var item in items)
        {
            try
            {
                // Convert dynamic to JSON and back to specific type
                var json = JsonSerializer.Serialize(item);
                var typed = JsonSerializer.Deserialize<T>(json);
                if (typed != null)
                {
                    result.Add(typed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting dynamic object to type {TargetType}", typeof(T).Name);
            }
        }

        return result;
    }
}
