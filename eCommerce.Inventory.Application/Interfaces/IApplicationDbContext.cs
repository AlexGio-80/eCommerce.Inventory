using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Game> Games { get; }
    DbSet<Expansion> Expansions { get; }
    DbSet<Blueprint> Blueprints { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
