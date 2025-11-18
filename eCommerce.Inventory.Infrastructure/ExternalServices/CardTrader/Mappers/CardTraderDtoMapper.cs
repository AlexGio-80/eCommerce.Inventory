using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;

/// <summary>
/// Maps Card Trader DTOs to Domain Entities
/// Following SPECIFICATIONS: Single Responsibility, Dependency Inversion
/// </summary>
public class CardTraderDtoMapper
{
    private readonly ILogger<CardTraderDtoMapper> _logger;

    public CardTraderDtoMapper(ILogger<CardTraderDtoMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Maps CardTraderGameDto to Game entity
    /// </summary>
    public Game MapGame(CardTraderGameDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null GameDto");
            throw new ArgumentNullException(nameof(dto), "GameDto cannot be null");
        }

        return new Game
        {
            CardTraderId = dto.Id,
            Name = dto.Name,
            Code = dto.DisplayName
        };
    }

    /// <summary>
    /// Maps multiple CardTraderGameDto to Game entities
    /// </summary>
    public List<Game> MapGames(List<CardTraderGameDto> dtos)
    {
        if (dtos == null || !dtos.Any())
        {
            _logger.LogInformation("No games to map");
            return new List<Game>();
        }

        _logger.LogInformation("Mapping {GameCount} games from Card Trader", dtos.Count);

        return dtos.Select(MapGame).ToList();
    }

    /// <summary>
    /// Maps CardTraderExpansionDto to Expansion entity
    /// </summary>
    public Expansion MapExpansion(CardTraderExpansionDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null ExpansionDto");
            throw new ArgumentNullException(nameof(dto), "ExpansionDto cannot be null");
        }

        // Generate name if not provided by API
        var name = string.IsNullOrWhiteSpace(dto.Name)
            ? $"Expansion {dto.Id}"
            : dto.Name;

        // Generate abbreviation if not provided by API
        var abbreviation = string.IsNullOrWhiteSpace(dto.Abbreviation)
            ? GenerateAbbreviation(name)
            : dto.Abbreviation;

        return new Expansion
        {
            CardTraderId = dto.Id,
            Name = name,
            Code = abbreviation,
            GameId = dto.GameId
        };
    }

    /// <summary>
    /// Generates an abbreviation from expansion name (first 3 uppercase letters)
    /// </summary>
    private string GenerateAbbreviation(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "N/A";
        }

        // Take first 3 letters, uppercase
        var abbreviation = new string(name
            .Where(c => !char.IsWhiteSpace(c))
            .Take(3)
            .ToArray())
            .ToUpper();

        return string.IsNullOrEmpty(abbreviation) ? "N/A" : abbreviation;
    }

    /// <summary>
    /// Maps multiple CardTraderExpansionDto to Expansion entities
    /// </summary>
    public List<Expansion> MapExpansions(List<CardTraderExpansionDto> dtos)
    {
        if (dtos == null || !dtos.Any())
        {
            _logger.LogInformation("No expansions to map");
            return new List<Expansion>();
        }

        _logger.LogInformation("Mapping {ExpansionCount} expansions from Card Trader", dtos.Count);

        return dtos.Select(MapExpansion).ToList();
    }

    /// <summary>
    /// Maps CardTraderBlueprintDto to Blueprint entity
    /// </summary>
    public Blueprint MapBlueprint(CardTraderBlueprintDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null BlueprintDto");
            throw new ArgumentNullException(nameof(dto), "BlueprintDto cannot be null");
        }

        return new Blueprint
        {
            CardTraderId = dto.Id,
            Name = dto.Name,
            Version = "Regular", // Default version (can be overridden later)
            Rarity = dto.Rarity,
            ExpansionId = dto.ExpansionId
            // Note: ImageUrl from DTO is not stored in Blueprint entity
        };
    }

    /// <summary>
    /// Maps multiple CardTraderBlueprintDto to Blueprint entities
    /// </summary>
    public List<Blueprint> MapBlueprints(List<CardTraderBlueprintDto> dtos)
    {
        if (dtos == null || !dtos.Any())
        {
            _logger.LogInformation("No blueprints to map");
            return new List<Blueprint>();
        }

        _logger.LogInformation("Mapping {BlueprintCount} blueprints from Card Trader", dtos.Count);

        return dtos.Select(MapBlueprint).ToList();
    }

    /// <summary>
    /// Maps CardTraderProductDto to InventoryItem entity
    /// Useful for creating new items from Card Trader products
    /// </summary>
    public InventoryItem MapProductToInventoryItem(CardTraderProductDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null ProductDto");
            throw new ArgumentNullException(nameof(dto), "ProductDto cannot be null");
        }

        return new InventoryItem
        {
            CardTraderProductId = dto.Id,
            BlueprintId = dto.BlueprintId,
            PurchasePrice = 0m, // Not available in DTO (user maintains purchase price locally)
            DateAdded = DateTime.UtcNow,
            Quantity = dto.Quantity,
            ListingPrice = dto.Price,
            Condition = dto.Condition,
            Language = dto.Language,
            IsFoil = dto.IsFoil,
            IsSigned = dto.IsSigned,
            Location = dto.UserDataField ?? "Unknown" // Default location if not provided
        };
    }

    /// <summary>
    /// Updates an existing InventoryItem with data from CardTraderProductDto
    /// Used for syncing product updates from Card Trader
    /// </summary>
    public void UpdateInventoryItemFromProduct(InventoryItem item, CardTraderProductDto dto)
    {
        if (item == null)
        {
            _logger.LogWarning("Attempted to update null InventoryItem");
            throw new ArgumentNullException(nameof(item), "InventoryItem cannot be null");
        }

        if (dto == null)
        {
            _logger.LogWarning("Attempted to update from null ProductDto");
            throw new ArgumentNullException(nameof(dto), "ProductDto cannot be null");
        }

        // Update fields that can change on the marketplace
        item.ListingPrice = dto.Price;
        item.Quantity = dto.Quantity;
        item.Condition = dto.Condition;
        item.Language = dto.Language;
        item.IsFoil = dto.IsFoil;
        item.IsSigned = dto.IsSigned;

        // Update location if provided
        if (!string.IsNullOrWhiteSpace(dto.UserDataField))
        {
            item.Location = dto.UserDataField;
        }

        _logger.LogInformation("Updated InventoryItem {ItemId} from Card Trader product {ProductId}",
            item.Id, dto.Id);
    }

    /// <summary>
    /// Maps multiple CardTraderProductDto to InventoryItem entities
    /// </summary>
    public List<InventoryItem> MapProductsToInventoryItems(List<CardTraderProductDto> dtos)
    {
        if (dtos == null || !dtos.Any())
        {
            _logger.LogInformation("No products to map");
            return new List<InventoryItem>();
        }

        _logger.LogInformation("Mapping {ProductCount} products to inventory items", dtos.Count);

        return dtos.Select(MapProductToInventoryItem).ToList();
    }

    /// <summary>
    /// Maps CardTraderCategoryDto to Category entity
    /// Includes mapping of properties and their possible values
    /// </summary>
    public Category MapCategory(CardTraderCategoryDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null CategoryDto");
            throw new ArgumentNullException(nameof(dto), "CategoryDto cannot be null");
        }

        var category = new Category
        {
            CardTraderId = dto.Id,
            Name = dto.Name,
            GameId = dto.GameId
        };

        // Map properties within the category
        if (dto.Properties != null && dto.Properties.Any())
        {
            category.Properties = dto.Properties.Select(p => MapProperty(p)).ToList();
        }

        return category;
    }

    /// <summary>
    /// Maps CardTraderPropertyDto to Property entity
    /// Includes mapping of possible values
    /// </summary>
    private Property MapProperty(CardTraderPropertyDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null PropertyDto");
            throw new ArgumentNullException(nameof(dto), "PropertyDto cannot be null");
        }

        var property = new Property
        {
            Name = dto.Name,
            Type = dto.Type
        };

        // Map possible values for this property
        if (dto.PossibleValues != null && dto.PossibleValues.Any())
        {
            property.PossibleValues = dto.PossibleValues
                .Select(v => new PropertyValue { Value = v.ToString() ?? string.Empty })
                .ToList();
        }

        return property;
    }

    /// <summary>
    /// Maps multiple CardTraderCategoryDto to Category entities
    /// </summary>
    public List<Category> MapCategories(List<CardTraderCategoryDto> dtos)
    {
        if (dtos == null || !dtos.Any())
        {
            _logger.LogInformation("No categories to map");
            return new List<Category>();
        }

        _logger.LogInformation("Mapping {CategoryCount} categories from Card Trader", dtos.Count);

        return dtos.Select(MapCategory).ToList();
    }
}
