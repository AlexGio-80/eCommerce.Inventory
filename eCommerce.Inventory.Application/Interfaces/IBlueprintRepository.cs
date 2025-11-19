using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Repository interface for Blueprint entity
/// Provides data access operations for card blueprints
/// </summary>
public interface IBlueprintRepository : IReadonlyRepository<Blueprint>
{
    /// <summary>
    /// Get blueprints with pagination
    /// </summary>
    Task<PagedResponse<Blueprint>> GetPagedAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get blueprints by game ID
    /// </summary>
    Task<IEnumerable<Blueprint>> GetByGameIdAsync(int gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get blueprints by expansion ID
    /// </summary>
    Task<IEnumerable<Blueprint>> GetByExpansionIdAsync(int expansionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get blueprint by Card Trader ID
    /// </summary>
    Task<Blueprint> GetByCardTraderIdAsync(int cardTraderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search blueprints by name
    /// </summary>
    Task<IEnumerable<Blueprint>> SearchByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total count of blueprints
    /// </summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
}
