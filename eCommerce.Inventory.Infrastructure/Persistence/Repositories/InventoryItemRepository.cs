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

    public async Task<(IEnumerable<InventoryItem> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.InventoryItems
            .Include(i => i.Blueprint)
            .ThenInclude(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(i => i.DateAdded)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
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

    /// <summary>
    /// Build a filtered query for inventory items (for server-side AG-Grid filtering)
    /// </summary>
    public IQueryable<InventoryItem> GetFilteredQuery(
        string? searchTerm = null,
        string? cardName = null,
        string? expansionName = null,
        string? condition = null,
        string? language = null)
    {
        var query = _context.InventoryItems
            .Include(i => i.Blueprint)
                .ThenInclude(b => b.Expansion)
                    .ThenInclude(e => e.Game)
            .AsNoTracking();

        // Global search across multiple fields
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(i =>
                i.Blueprint.Name.Contains(searchTerm) ||
                i.Blueprint.Expansion.Name.Contains(searchTerm) ||
                i.Condition.Contains(searchTerm) ||
                i.Language.Contains(searchTerm));
        }

        // Specific column filters
        if (!string.IsNullOrWhiteSpace(cardName))
        {
            query = query.Where(i => i.Blueprint.Name.Contains(cardName));
        }

        if (!string.IsNullOrWhiteSpace(expansionName))
        {
            query = query.Where(i => i.Blueprint.Expansion.Name.Contains(expansionName));
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            query = query.Where(i => i.Condition.Contains(condition));
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            query = query.Where(i => i.Language.Contains(language));
        }

        // Default ordering
        query = query.OrderByDescending(i => i.DateAdded);

        return query;
    }
}
