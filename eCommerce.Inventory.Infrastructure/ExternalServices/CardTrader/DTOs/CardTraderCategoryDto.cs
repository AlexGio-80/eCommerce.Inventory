using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Category API response
/// Represents a category that groups products with similar properties within a game
/// </summary>
public class CardTraderCategoryDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("game_id")]
    public int GameId { get; set; }

    [JsonPropertyName("properties")]
    public List<CardTraderPropertyDto> Properties { get; set; } = new();
}

/// <summary>
/// DTO for property within a category
/// Defines an attribute that can be used to describe products in this category
/// </summary>
public class CardTraderPropertyDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "string", "boolean", etc.

    [JsonPropertyName("possible_values")]
    public List<object> PossibleValues { get; set; } = new();
}

/// <summary>
/// Wrapper for Card Trader Categories API response
/// </summary>
public class CardTraderCategoriesResponseDto
{
    [JsonPropertyName("array")]
    public List<CardTraderCategoryDto> Array { get; set; } = new();
}
