using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

public class InventorySyncServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly CardTraderDtoMapper _mapper;
    private readonly Mock<ILogger<InventorySyncService>> _loggerMock;
    private readonly InventorySyncService _service;

    public InventorySyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        var mapperLoggerMock = new Mock<ILogger<CardTraderDtoMapper>>();
        _mapper = new CardTraderDtoMapper(mapperLoggerMock.Object);

        _loggerMock = new Mock<ILogger<InventorySyncService>>();

        _service = new InventorySyncService(_context, _mapper, _loggerMock.Object);
    }

    [Fact]
    public async Task SyncProductsAsync_ShouldInsertNewProduct_WhenProductDoesNotExist()
    {
        // Arrange
        var productDto = new CardTraderProductDto
        {
            Id = 100,
            BlueprintId = 5,
            PriceCents = 1000,
            Quantity = 2,
            Properties = new Dictionary<string, object>
            {
                { "condition", "NM" },
                { "language", "EN" },
                { "foil", false },
                { "signed", false }
            }
        };

        var dtos = new List<CardTraderProductDto> { productDto };

        // Act
        await _service.SyncProductsAsync(dtos);

        // Assert
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.CardTraderProductId == 100);
        item.Should().NotBeNull();
        item!.Quantity.Should().Be(2);
        item.ListingPrice.Should().Be(10.00m);
        item.Condition.Should().Be("NM");
        item.Language.Should().Be("EN");
    }

    [Fact]
    public async Task SyncProductsAsync_ShouldUpdateExistingProduct_WhenProductExists()
    {
        // Arrange
        var existingItem = new InventoryItem
        {
            CardTraderProductId = 200,
            BlueprintId = 10,
            Quantity = 1,
            ListingPrice = 5.00m,
            Condition = "LP",
            Language = "IT",
            Location = "Test Location",
            DateAdded = DateTime.UtcNow
        };
        _context.InventoryItems.Add(existingItem);
        await _context.SaveChangesAsync();

        var productDto = new CardTraderProductDto
        {
            Id = 200,
            BlueprintId = 10,
            PriceCents = 800, // Changed price
            Quantity = 5,     // Changed quantity
            Properties = new Dictionary<string, object>
            {
                { "condition", "NM" }, // Changed condition
                { "language", "EN" }   // Changed language
            }
        };

        var dtos = new List<CardTraderProductDto> { productDto };

        // Act
        await _service.SyncProductsAsync(dtos);

        // Assert
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.CardTraderProductId == 200);
        item.Should().NotBeNull();
        item!.Quantity.Should().Be(5);
        item.ListingPrice.Should().Be(8.00m);
        item.Condition.Should().Be("NM");
        item.Language.Should().Be("EN");
    }

    [Fact]
    public async Task SyncProductsAsync_ShouldHandleEmptyList_WithoutError()
    {
        // Arrange
        var dtos = new List<CardTraderProductDto>();

        // Act
        Func<Task> act = async () => await _service.SyncProductsAsync(dtos);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
