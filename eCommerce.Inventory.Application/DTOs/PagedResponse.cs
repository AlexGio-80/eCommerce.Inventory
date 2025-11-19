namespace eCommerce.Inventory.Application.DTOs;

/// <summary>
/// Generic paginated response for API endpoints
/// Provides pagination metadata along with the actual data
/// </summary>
/// <typeparam name="T">Type of items in the response</typeparam>
public class PagedResponse<T> where T : class
{
    /// <summary>
    /// Collection of items for the current page
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// Current page number (1-based)
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of items across all pages
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Total number of pages
    /// </summary>
    public int TotalPages => TotalCount > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

    /// <summary>
    /// Whether there is a next page
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Whether there is a previous page
    /// </summary>
    public bool HasPreviousPage => Page > 1;
}
