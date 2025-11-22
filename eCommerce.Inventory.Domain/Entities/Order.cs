namespace eCommerce.Inventory.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int CardTraderOrderId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string TransactionCode { get; set; } = string.Empty;

    public int BuyerId { get; set; }
    public string BuyerUsername { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public DateTime? SentAt { get; set; }

    public decimal SellerTotal { get; set; }
    public decimal SellerFee { get; set; }
    public decimal SellerSubtotal { get; set; }

    public string ShippingAddressJson { get; set; } = string.Empty;
    public string BillingAddressJson { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
