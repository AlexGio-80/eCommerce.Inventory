using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall.DTOs;

public class ScryfallSetDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("released_at")]
    public string? ReleasedAt { get; set; }

    [JsonPropertyName("icon_svg_uri")]
    public string? IconSvgUri { get; set; }
}

public class ScryfallSetsResponse
{
    [JsonPropertyName("data")]
    public List<ScryfallSetDto> Data { get; set; } = new();
}
