using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Application.DTOs;

public class CardTraderMarketplaceProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("blueprint_id")]
    public int BlueprintId { get; set; }

    [JsonPropertyName("price_cents")]
    public int PriceCents { get; set; }

    [JsonPropertyName("price_currency")]
    public string PriceCurrency { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public CardTraderMarketplacePropertiesDto Properties { get; set; } = new();

    [JsonPropertyName("properties_hash")]
    public Dictionary<string, object> PropertiesHash { get; set; } = new();

    [JsonPropertyName("user")]
    public CardTraderMarketplaceUserDto User { get; set; } = new();
}

public class CardTraderMarketplacePropertiesDto
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("mtg_foil")]
    public bool IsFoil { get; set; }

    [JsonPropertyName("signed")]
    public bool IsSigned { get; set; }

    [JsonPropertyName("altered")]
    public bool IsAltered { get; set; }
}

public class CardTraderMarketplaceUserDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("can_sell_via_hub")]
    public bool CanSellViaHub { get; set; }
}
