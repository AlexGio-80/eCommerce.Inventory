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

/// <summary>
/// DTO for a single Scryfall card response
/// </summary>
public class ScryfallCardDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("printed_name")]
    public string? PrintedName { get; set; }

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("collector_number")]
    public string? CollectorNumber { get; set; }

    [JsonPropertyName("set")]
    public string? Set { get; set; }

    [JsonPropertyName("set_name")]
    public string? SetName { get; set; }

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }

    [JsonPropertyName("image_uris")]
    public ScryfallImageUrisDto? ImageUris { get; set; }

    /// <summary>
    /// Localized names for this card (e.g. { "it": { "name": "...", "uri": "..." } })
    /// Present only for cards that have localized versions on Scryfall
    /// </summary>
    [JsonPropertyName("localized")]
    public Dictionary<string, ScryfallLocalizedNameDto>? Localized { get; set; }
}

/// <summary>
/// DTO for a Scryfall localized card name entry
/// </summary>
public class ScryfallLocalizedNameDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

/// <summary>
/// DTO for Scryfall card image URIs
/// </summary>
public class ScryfallImageUrisDto
{
    [JsonPropertyName("small")]
    public string? Small { get; set; }

    [JsonPropertyName("normal")]
    public string? Normal { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }

    [JsonPropertyName("png")]
    public string? Png { get; set; }

    [JsonPropertyName("art_crop")]
    public string? ArtCrop { get; set; }

    [JsonPropertyName("border_crop")]
    public string? BorderCrop { get; set; }
}
