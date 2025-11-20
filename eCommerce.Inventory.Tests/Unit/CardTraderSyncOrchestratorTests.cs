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
}
