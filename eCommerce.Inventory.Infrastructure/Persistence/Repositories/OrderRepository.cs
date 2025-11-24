using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Application.DTOs;

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

    public async Task<IEnumerable<Order>> GetOrdersWithItemsAsync(DateTime? from = null, DateTime? to = null, bool excludeNullDates = true, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .AsQueryable();

        // Apply date filters if provided
        if (from.HasValue || to.HasValue)
        {
            query = query.Where(o =>
                // Include orders with null PaidAt ONLY if excludeNullDates is false
                (!excludeNullDates && o.PaidAt == null) ||
                // Or orders within the date range
                ((!from.HasValue || o.PaidAt >= from.Value) &&
                 (!to.HasValue || o.PaidAt <= to.Value.AddDays(1).AddSeconds(-1))) // Include entire "to" day
            );
        }
        else if (excludeNullDates)
        {
            // If no date range provided but excludeNullDates is true, filter out nulls
            query = query.Where(o => o.PaidAt != null);
        }

        return await query
            .OrderByDescending(o => o.PaidAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UnpreparedItemDto>> GetUnpreparedItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _context.OrderItems
            .Include(oi => oi.Order)
            .Include(oi => oi.Blueprint)
            .ThenInclude(b => b.Expansion)
            .Where(oi => !oi.IsPrepared)
            .Select(oi => new UnpreparedItemDto
            {
                Id = oi.Id,
                Name = oi.Name,
                ExpansionName = oi.ExpansionName,
                ExpansionCode = oi.Blueprint != null && oi.Blueprint.Expansion != null ? oi.Blueprint.Expansion.Code : null,
                Condition = oi.Condition,
                Language = oi.Language,
                Quantity = oi.Quantity,
                Price = oi.Price,
                OrderCode = oi.Order != null ? oi.Order.Code : "N/A",
                BuyerUsername = oi.Order != null ? oi.Order.BuyerUsername : "N/A",
                OrderDate = oi.Order != null ? oi.Order.PaidAt : null,
                IsPrepared = oi.IsPrepared,
                ImageUrl = oi.Blueprint != null ? oi.Blueprint.ImageUrl : null,
                IsFoil = oi.IsFoil,
                IsSigned = oi.IsSigned,
                IsAltered = oi.IsAltered,
                Tag = oi.UserDataField,
                CollectorNumber = oi.Blueprint != null ? oi.Blueprint.FixedProperties : null,
                CardTraderBlueprintId = oi.Blueprint != null ? oi.Blueprint.CardTraderId : null
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.CollectorNumber))
            {
                try
                {
                    using (var doc = System.Text.Json.JsonDocument.Parse(item.CollectorNumber))
                    {
                        if (doc.RootElement.TryGetProperty("collector_number", out var prop))
                        {
                            item.CollectorNumber = prop.GetString();
                        }
                        else
                        {
                            item.CollectorNumber = null;
                        }
                    }
                }
                catch
                {
                    item.CollectorNumber = null;
                }
            }
        }

        return items;
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
