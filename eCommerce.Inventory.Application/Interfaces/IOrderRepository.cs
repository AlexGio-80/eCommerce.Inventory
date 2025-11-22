using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Application.DTOs;

namespace eCommerce.Inventory.Application.Interfaces;

public interface IOrderRepository : IReadonlyRepository<Order>
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<Order?> GetByCardTraderIdAsync(int cardTraderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetOrdersWithItemsAsync(DateTime? from = null, DateTime? to = null, bool excludeNullDates = true, CancellationToken cancellationToken = default);
    Task<IEnumerable<UnpreparedItemDto>> GetUnpreparedItemsAsync(CancellationToken cancellationToken = default);
}
