namespace eCommerce.Inventory.Domain.Entities;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public virtual Order Order { get; set; }

    public int CardTraderId { get; set; }
    public int ProductId { get; set; }
    public int? BlueprintId { get; set; }
    public virtual Blueprint? Blueprint { get; set; }
    public int CategoryId { get; set; }
    public int GameId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ExpansionName { get; set; } = string.Empty;

    public int Quantity { get; set; }
    public decimal Price { get; set; }

    public string Condition { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsFoil { get; set; }
    public bool IsSigned { get; set; }
    public bool IsAltered { get; set; }

    public string? UserDataField { get; set; }
    public string? Tag { get; set; }

    public bool IsPrepared { get; set; }

    /// <summary>
    /// Su una riga di un ordine "Ct connect" pagato (Card Trader Zero): la riga "hub_pending" della stessa
    /// vendita, a cui è stata abbinata. Le vendite Card Trader Zero arrivano prima come ordini "hub_pending"
    /// singoli, che si preparano giorno per giorno, e poi raccolte in un ordine pagato con le stesse righe.
    /// Null se non ancora abbinata. Vedi <c>CtZeroPreparationReconciler</c>.
    /// </summary>
    public int? HubOrderItemId { get; set; }
}
