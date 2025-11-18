using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Expansion/Set API response
/// </summary>
public class CardTraderExpansionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("code")]
    public string Abbreviation { get; set; } // Es. "gnt", "grn" (mapped from 'code' in JSON)

    [JsonPropertyName("game_id")]
    public int GameId { get; set; }
}
