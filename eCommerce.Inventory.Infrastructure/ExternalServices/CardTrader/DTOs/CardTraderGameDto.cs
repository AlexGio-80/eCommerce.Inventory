using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Game API response
/// </summary>
public class CardTraderGameDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; }
}

/// <summary>
/// Wrapper for Card Trader Games API response
/// </summary>
public class CardTraderGamesResponseDto
{
    [JsonPropertyName("array")]
    public List<CardTraderGameDto> Array { get; set; } = new();
}
