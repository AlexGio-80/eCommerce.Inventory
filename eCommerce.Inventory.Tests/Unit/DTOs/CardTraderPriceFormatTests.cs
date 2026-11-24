using System.Text.Json;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.DTOs;

/// <summary>
/// Il 24/11/2026 Card Trader ha tolto "price_cents" da marketplace/products lasciando solo
/// "price": { "cents", "currency" }: ogni offerta risultava a 0 €.
/// </summary>
public class CardTraderPriceFormatTests
{
    [Fact]
    public void Marketplace_reads_new_price_object()
    {
        var dto = JsonSerializer.Deserialize<CardTraderMarketplaceProductDto>(
            """{ "id": 1, "blueprint_id": 2, "price": { "cents": 1321, "currency": "EUR" } }""")!;

        Assert.Equal(1321, dto.PriceCents);
        Assert.Equal("EUR", dto.PriceCurrency);
    }

    [Fact]
    public void Marketplace_still_reads_old_price_cents()
    {
        var dto = JsonSerializer.Deserialize<CardTraderMarketplaceProductDto>(
            """{ "id": 1, "blueprint_id": 2, "price_cents": 500, "price_currency": "EUR" }""")!;

        Assert.Equal(500, dto.PriceCents);
        Assert.Equal("EUR", dto.PriceCurrency);
    }

    [Fact]
    public void Export_reads_either_format()
    {
        var oldFormat = JsonSerializer.Deserialize<CardTraderProductDto>("""{ "id": 1, "price_cents": 5 }""")!;
        var newFormat = JsonSerializer.Deserialize<CardTraderProductDto>("""{ "id": 1, "price": { "cents": 7, "currency": "EUR" } }""")!;

        Assert.Equal(5, oldFormat.PriceCents);
        Assert.Equal(7, newFormat.PriceCents);
    }
}
