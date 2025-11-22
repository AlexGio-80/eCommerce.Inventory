using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Infrastructure.Persistence.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;

    public OrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Order> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .OrderByDescending(o => o.PaidAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetOrdersWithItemsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Orders.AddAsync(order, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetByIdAsync(id, cancellationToken);
        if (order != null)
        {
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Order?> GetByCardTraderIdAsync(int cardTraderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.CardTraderOrderId == cardTraderId, cancellationToken);
    }
}
