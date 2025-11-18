namespace eCommerce.Inventory.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int CardTraderOrderId { get; set; }
    public DateTime DatePlaced { get; set; }
    public string Status { get; set; } // Es. "Paid", "Shipped", "Cancelled"
    public decimal TotalAmount { get; set; }
    public decimal ShippingCost { get; set; }

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
