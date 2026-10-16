namespace eCommerce.Inventory.Application.DTOs;

/// <summary>
/// Riga del carrello Card Trader dell'account (GET /cart): l'offerta del marketplace e quante unità
/// ci sono. Un'offerta compare una volta sola anche se aggiunta più volte (la quantità sale).
/// </summary>
/// <param name="ProductId">Id dell'offerta sul marketplace (lo stesso di <c>product_id</c> di /cart/add).</param>
/// <param name="SellerName">Venditore del sotto-carrello, se Card Trader lo indica.</param>
public record CardTraderCartLine(int ProductId, int Quantity, string? Name, string? SellerName);
