using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Blueprint entity
/// Provides CRUD and query operations for card blueprints
/// </summary>
public class BlueprintRepository : IBlueprintRepository
{
    private readonly ApplicationDbContext _context;

    public BlueprintRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get blueprint by ID with related entities
    /// </summary>
    public async Task<Blueprint> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Blueprints
            .Include(b => b.Game)
            .Include(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .Include(b => b.InventoryItems)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    /// <summary>
    /// Get all blueprints with related entities
    /// </summary>
    public async Task<IEnumerable<Blueprint>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Blueprints
            .Include(b => b.Game)
            .Include(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get blueprints with pagination support
    /// </summary>
    public async Task<PagedResponse<Blueprint>> GetPagedAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100; // Max 100 per page

        var query = _context.Blueprints
            .Include(b => b.Game)
            .Include(b => b.Expansion)
            .ThenInclude(e => e.Game)
            .AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(b => b.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Blueprint>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Get blueprints by game ID
    /// </summary>
    public async Task<IEnumerable<Blueprint>> GetByGameIdAsync(
        int gameId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Blueprints
            .Include(b => b.Expansion)
            .Where(b => b.GameId == gameId)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get blueprints by expansion ID
    /// </summary>
    public async Task<IEnumerable<Blueprint>> GetByExpansionIdAsync(
        int expansionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Blueprints
            .Include(b => b.Game)
            .Include(b => b.Expansion)
            .Where(b => b.ExpansionId == expansionId)
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get blueprint by Card Trader ID
    /// Useful for checking if blueprint already exists during sync
    /// </summary>
    public async Task<Blueprint> GetByCardTraderIdAsync(
        int cardTraderId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Blueprints
            .Include(b => b.Game)
            .Include(b => b.Expansion)
            .FirstOrDefaultAsync(b => b.CardTraderId == cardTraderId, cancellationToken);
    }

    /// <summary>
    /// Search blueprints by name (case-insensitive partial match)
    /// </summary>
    public async Task<IEnumerable<Blueprint>> SearchByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new List<Blueprint>();
        }

        var searchTerm = name.ToLower().Trim();

        return await _context.Blueprints
            .Include(b => b.Game)
            .Include(b => b.Expansion)
            .Where(b => b.Name.ToLower().Contains(searchTerm))
            .OrderBy(b => b.Name)
            .Take(50) // Limit search results to 50
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get total count of blueprints
    /// </summary>
    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Blueprints.CountAsync(cancellationToken);
    }
}
