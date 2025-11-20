using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

public class CardTraderProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("blueprint_id")]
    public int BlueprintId { get; set; }

    [JsonPropertyName("price_cents")]
    public int PriceCents { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("game_id")]
    public int GameId { get; set; }

    [JsonPropertyName("category_id")]
    public int CategoryId { get; set; }

    [JsonPropertyName("properties_hash")]
    public Dictionary<string, object> Properties { get; set; } = new();

    [JsonPropertyName("expansion")]
    public CardTraderExpansionReferenceDto Expansion { get; set; }

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; }

    [JsonPropertyName("user_data_field")]
    public string UserDataField { get; set; }
}

public class CardTraderExpansionReferenceDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
