using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit;

public class CardTraderSyncOrchestratorTests
{
    private readonly Mock<ICardTraderApiService> _apiServiceMock;
    private readonly Mock<ILogger<CardTraderSyncOrchestrator>> _loggerMock;
    private readonly Mock<ILogger<CardTraderDtoMapper>> _mapperLoggerMock;
    private readonly ApplicationDbContext _dbContext;
    private readonly CardTraderDtoMapper _mapper;
    private readonly InventorySyncService _inventorySyncService;
    private readonly CardTraderSyncOrchestrator _orchestrator;

    public CardTraderSyncOrchestratorTests()
    {
        _apiServiceMock = new Mock<ICardTraderApiService>();
        _loggerMock = new Mock<ILogger<CardTraderSyncOrchestrator>>();
        _mapperLoggerMock = new Mock<ILogger<CardTraderDtoMapper>>();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _mapper = new CardTraderDtoMapper(_mapperLoggerMock.Object);

        // Mock InventorySyncService dependencies if needed, or use a real one with mocked dependencies
        // For now, we can pass null or a mock if we don't test Categories sync deeply
        // But Orchestrator constructor requires it.
        // Let's mock the service or create a simple one.
        // InventorySyncService depends on DbContext and Logger.
        var inventoryLogger = new Mock<ILogger<InventorySyncService>>();
        _inventorySyncService = new InventorySyncService(_dbContext, _mapper, inventoryLogger.Object);

        _orchestrator = new CardTraderSyncOrchestrator(
            _apiServiceMock.Object,
            _mapper,
            _dbContext,
            _inventorySyncService,
            _loggerMock.Object);
    }

    [Fact]
    public async Task SyncAsync_SyncBlueprints_ShouldSyncBlueprintsForEnabledGames()
    {
        // Arrange
        var game = new Game { Id = 1, CardTraderId = 100, Name = "Magic", Code = "MTG", IsEnabled = true };
        var expansion = new Expansion { Id = 1, CardTraderId = 200, GameId = 1, Name = "Alpha", Code = "LEA" };

        _dbContext.Games.Add(game);
        _dbContext.Expansions.Add(expansion);
        await _dbContext.SaveChangesAsync();

        var blueprintDto = new CardTraderBlueprintDto
        {
            Id = 300,
            Name = "Black Lotus",
            ExpansionId = 200,
            GameId = 100,
            FixedProperties = new Dictionary<string, object> { { "rarity", "Rare" } }
        };

        _apiServiceMock.Setup(x => x.SyncBlueprintsForExpansionAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic> { blueprintDto });

        var request = new SyncRequestDto { SyncBlueprints = true };

        // Act
        var result = await _orchestrator.SyncAsync(request);

        // Assert
        Assert.Equal(1, result.Blueprints.Added);
        Assert.Equal(0, result.Blueprints.Failed);

        var blueprint = await _dbContext.Blueprints.FirstOrDefaultAsync(b => b.CardTraderId == 300);
        Assert.NotNull(blueprint);
        Assert.Equal("Black Lotus", blueprint.Name);
        Assert.Equal("Rare", blueprint.Rarity);
    }

    [Fact]
    public async Task SyncAsync_SyncBlueprints_ShouldSkipDisabledGames()
    {
        // Arrange
        var game = new Game { Id = 1, CardTraderId = 100, Name = "Pokemon", Code = "PKM", IsEnabled = false };
        var expansion = new Expansion { Id = 1, CardTraderId = 200, GameId = 1, Name = "Base Set", Code = "BS" };

        _dbContext.Games.Add(game);
        _dbContext.Expansions.Add(expansion);
        await _dbContext.SaveChangesAsync();

        var request = new SyncRequestDto { SyncBlueprints = true };

        // Act
        var result = await _orchestrator.SyncAsync(request);

        // Assert
        _apiServiceMock.Verify(x => x.SyncBlueprintsForExpansionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, result.Blueprints.Added);
    }
    [Fact]
    public async Task SyncAsync_SyncExpansions_ShouldSkipForMissingGame()
    {
        // Arrange
        // Game 999 does NOT exist in DB
        var expansionDto = new CardTraderExpansionDto
        {
            Id = 500,
            Name = "Orphan Expansion",
            GameId = 999,
            Abbreviation = "ORP"
        };

        _apiServiceMock.Setup(x => x.SyncExpansionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic> { expansionDto });

        var request = new SyncRequestDto { SyncExpansions = true };

        // Act
        var result = await _orchestrator.SyncAsync(request);

        // Assert
        Assert.Equal(0, result.Expansions.Added);
        Assert.Equal(0, result.Expansions.Failed);
        Assert.Equal(1, result.Expansions.Skipped);

        // Verify it wasn't added to DB
        var expansion = await _dbContext.Expansions.FirstOrDefaultAsync(e => e.CardTraderId == 500);
        Assert.Null(expansion);
    }
    [Fact]
    public async Task SyncAsync_SyncInventory_ShouldSyncAndHandleDeletions()
    {
        // Arrange
        var game = new Game { Id = 1, CardTraderId = 100, Name = "Magic", Code = "MTG", IsEnabled = true };
        var expansion = new Expansion { Id = 1, CardTraderId = 200, GameId = 1, Name = "Alpha", Code = "LEA" };
        var blueprint1 = new Blueprint { Id = 1, CardTraderId = 301, ExpansionId = 1, GameId = 1, Name = "Card 1", Version = "Regular" };
        var blueprint2 = new Blueprint { Id = 2, CardTraderId = 302, ExpansionId = 1, GameId = 1, Name = "Card 2", Version = "Regular" };
        var blueprint3 = new Blueprint { Id = 3, CardTraderId = 303, ExpansionId = 1, GameId = 1, Name = "Card 3", Version = "Regular" };

        _dbContext.Games.Add(game);
        _dbContext.Expansions.Add(expansion);
        _dbContext.Blueprints.AddRange(blueprint1, blueprint2, blueprint3);

        // Existing item to be UPDATED
        var itemToUpdate = new InventoryItem
        {
            Id = 1,
            CardTraderProductId = 1001,
            BlueprintId = 1,
            Quantity = 1,
            ListingPrice = 10.0m,
            Condition = "Played",
            Language = "en",
            Location = "Box 1",
            IsFoil = false,
            IsSigned = false
        };

        // Existing item to be DELETED (missing from API)
        var itemToDelete = new InventoryItem
        {
            Id = 2,
            CardTraderProductId = 1002,
            BlueprintId = 2,
            Quantity = 5,
            ListingPrice = 5.0m,
            Condition = "Near Mint",
            Language = "en",
            Location = "Box 2",
            IsFoil = false,
            IsSigned = false
        };

        _dbContext.InventoryItems.AddRange(itemToUpdate, itemToDelete);
        await _dbContext.SaveChangesAsync();

        // API Response
        var productDto1 = new CardTraderProductDto // Update for item 1001
        {
            Id = 1001,
            BlueprintId = 301,
            PriceCents = 2000, // 20.00
            Quantity = 2,
            GameId = 100,
            Properties = new Dictionary<string, object> { { "condition", "Near Mint" } },
            UserDataField = "Box A"
        };

        var productDto2 = new CardTraderProductDto // New item
        {
            Id = 1003,
            BlueprintId = 303, // Matches blueprint3
            PriceCents = 5000, // 50.00
            Quantity = 1,
            GameId = 100,
            Properties = new Dictionary<string, object> { { "condition", "Mint" } },
            UserDataField = "Box B"
        };

        _apiServiceMock.Setup(x => x.GetProductsExportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<dynamic> { productDto1, productDto2 });

        var request = new SyncRequestDto { SyncInventory = true };

        // Act
        var result = await _orchestrator.SyncAsync(request);

        // Assert
        Assert.Equal(1, result.Inventory.Added);   // Item 1003
        Assert.Equal(1, result.Inventory.Updated); // Item 1001
        // Deleted count is not in DTO, but we can verify DB state

        var updatedItem = await _dbContext.InventoryItems.FirstOrDefaultAsync(i => i.CardTraderProductId == 1001);
        Assert.NotNull(updatedItem);
        Assert.Equal(2, updatedItem.Quantity);
        Assert.Equal(20.0m, updatedItem.ListingPrice);
        Assert.Equal("Near Mint", updatedItem.Condition);
        Assert.Equal("Box A", updatedItem.Location);

        var newItem = await _dbContext.InventoryItems.FirstOrDefaultAsync(i => i.CardTraderProductId == 1003);
        Assert.NotNull(newItem);
        Assert.Equal(3, newItem.BlueprintId); // Linked to local Blueprint ID 3
        Assert.Equal(50.0m, newItem.ListingPrice);
        Assert.Equal("Box B", newItem.Location);

        var deletedItem = await _dbContext.InventoryItems.FirstOrDefaultAsync(i => i.CardTraderProductId == 1002);
        Assert.Null(deletedItem);
    }
}
