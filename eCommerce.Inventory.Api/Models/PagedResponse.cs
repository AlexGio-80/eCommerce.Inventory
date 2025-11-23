namespace eCommerce.Inventory.Api.Models;

/// <summary>
/// Standard pagination response for collection endpoints
/// Following SPECIFICATIONS: Consistent Pagination Pattern
/// </summary>
public class PagedResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }

    /// <summary>
    /// Create a paged response from items and metadata
    /// </summary>
    public static PagedResponse<T> Create(List<T> items, int page, int pageSize, int totalCount)
    {
        return new PagedResponse<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
