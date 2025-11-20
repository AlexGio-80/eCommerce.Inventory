using System.Text.Json;
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
    /// Converts complex properties (fixed_properties, editable_properties, card_market_ids) to JSON strings
    /// </summary>
    public Blueprint MapBlueprint(CardTraderBlueprintDto dto)
    {
        if (dto == null)
        {
            _logger.LogWarning("Attempted to map null BlueprintDto");
            throw new ArgumentNullException(nameof(dto), "BlueprintDto cannot be null");
        }

        try
        {
            var blueprint = new Blueprint
            {
                CardTraderId = dto.Id,
                Name = dto.Name,
                Version = string.IsNullOrWhiteSpace(dto.Version) ? "Regular" : dto.Version,
                GameId = dto.GameId,
                CategoryId = dto.CategoryId,
                Rarity = ExtractRarityFromFixedProperties(dto.FixedProperties),
                ExpansionId = dto.ExpansionId,
                ImageUrl = dto.ImageUrl,
                BackImageUrl = ExtractBackImageUrl(dto.BackImageUrl),
                TcgPlayerId = dto.TcgPlayerId,
                ScryfallId = dto.ScryfallId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Serialize fixed_properties to JSON string
            if (dto.FixedProperties != null && dto.FixedProperties.Any())
            {
                blueprint.FixedProperties = JsonSerializer.Serialize(dto.FixedProperties);
            }

            // Serialize editable_properties to JSON string
            if (dto.EditableProperties != null && dto.EditableProperties.Any())
            {
                blueprint.EditableProperties = JsonSerializer.Serialize(dto.EditableProperties);
            }

            // Serialize card_market_ids to JSON string
            if (dto.CardMarketIds != null && dto.CardMarketIds.Any())
            {
                blueprint.CardMarketIds = JsonSerializer.Serialize(dto.CardMarketIds);
            }

            return blueprint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping blueprint {BlueprintId} {BlueprintName}", dto.Id, dto.Name);
            throw;
        }
    }

    /// <summary>
    /// Extracts rarity from fixed_properties dictionary
    /// Looks for common rarity property names: mtg_rarity, rarity, etc.
    /// </summary>
    private string ExtractRarityFromFixedProperties(Dictionary<string, object> fixedProperties)
    {
        if (fixedProperties == null || !fixedProperties.Any())
        {
            return null;
        }

        // Try common rarity property names
        var rarityKey = fixedProperties.Keys
            .FirstOrDefault(k => k.Equals("mtg_rarity", StringComparison.OrdinalIgnoreCase) ||
                                 k.Equals("rarity", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(rarityKey) && fixedProperties[rarityKey] != null)
        {
            return fixedProperties[rarityKey].ToString();
        }

        return null;
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
            ListingPrice = dto.PriceCents / 100m, // Convert cents to decimal currency
            Condition = ExtractCondition(dto.Properties),
            Language = ExtractLanguage(dto.Properties),
            IsFoil = ExtractBooleanProperty(dto.Properties, "foil"),
            IsSigned = ExtractBooleanProperty(dto.Properties, "signed"),
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
        item.ListingPrice = dto.PriceCents / 100m; // Convert cents to decimal currency
        item.Quantity = dto.Quantity;
        item.Condition = ExtractCondition(dto.Properties);
        item.Language = ExtractLanguage(dto.Properties);
        item.IsFoil = ExtractBooleanProperty(dto.Properties, "foil");
        item.IsSigned = ExtractBooleanProperty(dto.Properties, "signed");

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

    private string ExtractCondition(Dictionary<string, object> properties)
    {
        if (properties == null) return "Unknown";
        if (properties.TryGetValue("condition", out var value)) return value?.ToString() ?? "Unknown";
        return "Unknown";
    }

    private string ExtractLanguage(Dictionary<string, object> properties)
    {
        if (properties == null) return "Unknown";
        // Check for common language keys
        string[] keys = { "language", "mtg_language", "pokemon_language", "yugioh_language" };
        foreach (var key in keys)
        {
            if (properties.TryGetValue(key, out var value)) return value?.ToString() ?? "Unknown";
        }
        return "Unknown";
    }

    private bool ExtractBooleanProperty(Dictionary<string, object> properties, string partialKey)
    {
        if (properties == null) return false;

        // Look for keys containing the partial key (e.g. "mtg_foil", "foil")
        var key = properties.Keys.FirstOrDefault(k => k.Contains(partialKey, StringComparison.OrdinalIgnoreCase));

        if (key != null && properties.TryGetValue(key, out var value))
        {
            if (value is bool boolValue) return boolValue;
            if (bool.TryParse(value?.ToString(), out var parsedBool)) return parsedBool;
        }

        return false;
    }

    /// <summary>
    /// Extracts back image URL from JsonElement (can be string or object)
    /// </summary>
    private string? ExtractBackImageUrl(System.Text.Json.JsonElement? backImage)
    {
        if (!backImage.HasValue)
            return null;

        try
        {
            // If it's a string, return it directly
            if (backImage.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return backImage.Value.GetString();
            }
            // If it's an object, try to extract 'url' property
            else if (backImage.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (backImage.Value.TryGetProperty("url", out var urlProperty))
                {
                    return urlProperty.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting back image URL");
        }

        return null;
    }
}
