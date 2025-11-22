using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

public class CardTraderOrderItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("blueprint_id")]
    public int? BlueprintId { get; set; }

    [JsonPropertyName("category_id")]
    public int? CategoryId { get; set; }

    [JsonPropertyName("game_id")]
    public int? GameId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("expansion")]
    public string Expansion { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("user_data_field")]
    public string? UserDataField { get; set; }

    [JsonPropertyName("properties")]
    public CardTraderItemPropertiesDto Properties { get; set; }

    [JsonPropertyName("seller_price")]
    public CardTraderPriceDto SellerPrice { get; set; }
}

public class CardTraderItemPropertiesDto
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; }

    [JsonPropertyName("mtg_language")]
    public string Language { get; set; }

    [JsonPropertyName("mtg_foil")]
    public bool Foil { get; set; }

    [JsonPropertyName("signed")]
    public bool Signed { get; set; }

    [JsonPropertyName("altered")]
    public bool Altered { get; set; }
}
