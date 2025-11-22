using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

public interface IOrderRepository : IReadonlyRepository<Order>
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<Order?> GetByCardTraderIdAsync(int cardTraderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetOrdersWithItemsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
}
