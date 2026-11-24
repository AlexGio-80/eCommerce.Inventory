using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

public class CardTraderProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("blueprint_id")]
    public int BlueprintId { get; set; }

    private int _priceCents;

    /// <summary>
    /// Prezzo in centesimi. Il 24/11/2026 Card Trader ha tolto "price_cents" dal marketplace
    /// lasciando solo "price": { "cents" }; l'export lo ha ancora, ma se cambia anche lui la
    /// sincronizzazione scriverebbe 0 su ogni inserzione. Si legge l'uno o l'altro.
    /// </summary>
    [JsonPropertyName("price_cents")]
    public int PriceCents
    {
        get => _priceCents > 0 ? _priceCents : Price?.Cents ?? 0;
        set => _priceCents = value;
    }

    [JsonPropertyName("price")]
    public CardTraderPriceDto? Price { get; set; }

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

    [JsonPropertyName("description")]
    public string Description { get; set; }
}

public class CardTraderExpansionReferenceDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
