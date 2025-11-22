using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;

public class CardTraderOrderDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }

    [JsonPropertyName("transaction_code")]
    public string TransactionCode { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; }

    [JsonPropertyName("paid_at")]
    public DateTime? PaidAt { get; set; }

    [JsonPropertyName("sent_at")]
    public DateTime? SentAt { get; set; }

    [JsonPropertyName("buyer")]
    public CardTraderUserDto Buyer { get; set; }

    [JsonPropertyName("seller_total")]
    public CardTraderPriceDto SellerTotal { get; set; }

    [JsonPropertyName("seller_fee_amount")]
    public CardTraderPriceDto SellerFeeAmount { get; set; }

    [JsonPropertyName("seller_subtotal")]
    public CardTraderPriceDto SellerSubtotal { get; set; }

    [JsonPropertyName("order_shipping_address")]
    public CardTraderAddressDto OrderShippingAddress { get; set; }

    [JsonPropertyName("order_billing_address")]
    public CardTraderAddressDto OrderBillingAddress { get; set; }

    [JsonPropertyName("order_items")]
    public List<CardTraderOrderItemDto> OrderItems { get; set; }
}

public class CardTraderUserDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }
}

public class CardTraderPriceDto
{
    [JsonPropertyName("cents")]
    public int Cents { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; }
}

public class CardTraderAddressDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("street")]
    public string Street { get; set; }

    [JsonPropertyName("zip")]
    public string Zip { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; }

    [JsonPropertyName("state_or_province")]
    public string StateOrProvince { get; set; }

    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
}
