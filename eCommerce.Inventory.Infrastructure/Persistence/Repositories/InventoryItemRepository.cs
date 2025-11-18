using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Infrastructure.Persistence.Repositories;

public class InventoryItemRepository : IInventoryItemRepository
{
    private readonly ApplicationDbContext _context;

    public InventoryItemRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryItem> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.InventoryItems
            .Include(i => i.Blueprint)
            .ThenInclude(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<InventoryItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.InventoryItems
            .Include(i => i.Blueprint)
            .ThenInclude(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        await _context.InventoryItems.AddAsync(item, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        _context.InventoryItems.Update(item);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await GetByIdAsync(id, cancellationToken);
        if (item != null)
        {
            _context.InventoryItems.Remove(item);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<InventoryItem> GetByCardTraderProductIdAsync(int cardTraderProductId, CancellationToken cancellationToken = default)
    {
        return await _context.InventoryItems
            .Include(i => i.Blueprint)
            .ThenInclude(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .FirstOrDefaultAsync(i => i.CardTraderProductId == cardTraderProductId, cancellationToken);
    }
}
