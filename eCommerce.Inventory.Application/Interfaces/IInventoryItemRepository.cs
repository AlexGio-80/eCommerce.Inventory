using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

public interface IInventoryItemRepository : IReadonlyRepository<InventoryItem>
{
    Task<IEnumerable<InventoryItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<(IEnumerable<InventoryItem> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<InventoryItem> GetByCardTraderProductIdAsync(int cardTraderProductId, CancellationToken cancellationToken = default);
}
