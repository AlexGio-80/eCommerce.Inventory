namespace eCommerce.Inventory.Application.Interfaces;

public interface IReadonlyRepository<T> where T : class
{
    Task<T> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
}
