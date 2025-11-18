using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

/// <summary>
/// DTO for Card Trader Webhook payload
/// Based on Card Trader Full API webhook documentation
/// </summary>
public class WebhookDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("cause")]
    public string Cause { get; set; } // "order.create", "order.update", "order.destroy"

    [JsonPropertyName("object_class")]
    public string ObjectClass { get; set; } // "Order"

    [JsonPropertyName("object_id")]
    public int ObjectId { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } // "test" or "live"

    [JsonPropertyName("data")]
    public CardTraderOrderDto Data { get; set; }
}

/// <summary>
/// DTO for webhook signature verification request
/// </summary>
public class WebhookRequest
{
    public WebhookDto Webhook { get; set; }
    public string Signature { get; set; } // From X-Signature header or similar
}
