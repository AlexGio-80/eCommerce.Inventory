using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Blueprint (Card) API response
/// Represents a card/blueprint with all metadata from Card Trader API
/// </summary>
public class CardTraderBlueprintDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("game_id")]
    public int GameId { get; set; }

    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    [JsonPropertyName("expansion_id")]
    public int ExpansionId { get; set; }

    /// <summary>
    /// Fixed properties (e.g., rarity, collector_number) stored as JSON
    /// These properties are set by Card Trader and cannot be changed when creating products
    /// Example: { "mtg_rarity": "Mythic", "collector_number": "127" }
    /// </summary>
    [JsonPropertyName("fixed_properties")]
    public Dictionary<string, object> FixedProperties { get; set; }

    /// <summary>
    /// Editable properties that can be set when creating a product listing
    /// Example: [{ "name": "condition", "type": "string", "possible_values": [...] }, ...]
    /// </summary>
    [JsonPropertyName("editable_properties")]
    public List<EditablePropertyDto> EditableProperties { get; set; }

    /// <summary>
    /// Card Market IDs (array of IDs on Cardmarket platform)
    /// </summary>
    [JsonPropertyName("card_market_ids")]
    public List<int> CardMarketIds { get; set; }

    /// <summary>
    /// TCGPlayer ID
    /// </summary>
    [JsonPropertyName("tcg_player_id")]
    public int? TcgPlayerId { get; set; }

    /// <summary>
    /// Scryfall ID (Magic: The Gathering only)
    /// </summary>
    [JsonPropertyName("scryfall_id")]
    public string ScryfallId { get; set; }

    /// <summary>
    /// Public URL to card image
    /// </summary>
    [JsonPropertyName("image_url")]
    public string ImageUrl { get; set; }

    /// <summary>
    /// Back image (for double-faced cards)
    /// Can be either a string URL or an object with image data
    /// </summary>
    [JsonPropertyName("back_image")]
    public System.Text.Json.JsonElement? BackImageUrl { get; set; }
}

/// <summary>
/// Represents an editable property that can be set when creating a product
/// </summary>
public class EditablePropertyDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("default_value")]
    public object DefaultValue { get; set; }

    [JsonPropertyName("possible_values")]
    public List<object> PossibleValues { get; set; }
}
