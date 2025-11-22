using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using Microsoft.AspNetCore.Mvc;

namespace eCommerce.Inventory.Api.Controllers.CardTrader;

[ApiController]
[Route("api/cardtrader/orders")]
public class CardTraderOrdersController : ControllerBase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICardTraderApiService _cardTraderApiService;
    private readonly InventorySyncService _inventorySyncService;
    private readonly ILogger<CardTraderOrdersController> _logger;
    private readonly IApplicationDbContext _context;

    public CardTraderOrdersController(
        IOrderRepository orderRepository,
        ICardTraderApiService cardTraderApiService,
        InventorySyncService inventorySyncService,
        ILogger<CardTraderOrdersController> logger,
        IApplicationDbContext context)
    {
        _orderRepository = orderRepository;
        _cardTraderApiService = cardTraderApiService;
        _inventorySyncService = inventorySyncService;
        _logger = logger;
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetOrders(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        _logger.LogInformation("Getting orders with filters - from: {From}, to: {To}", from, to);

        var orders = await _orderRepository.GetOrdersWithItemsAsync(from, to);

        _logger.LogInformation("Retrieved {Count} orders", orders.Count());

        return Ok(orders);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncOrders([FromBody] SyncOrdersRequest? request = null)
    {
        try
        {
            var from = request?.From;
            var to = request?.To;

            _logger.LogInformation("Manual sync of orders triggered");
            _logger.LogInformation("Received parameters - from: {From}, to: {To}", from, to);

            var orderDtos = await _cardTraderApiService.GetOrdersAsync(from, to);

            // Cast dynamic list to concrete DTO list
            // Note: GetOrdersAsync returns List<dynamic> but underlying objects are CardTraderOrderDto
            // because we deserialized them as such in CardTraderApiClient
            var concreteDtos = ((IEnumerable<dynamic>)orderDtos).Cast<Infrastructure.ExternalServices.CardTrader.DTOs.CardTraderOrderDto>().ToList();

            await _inventorySyncService.SyncOrdersAsync(concreteDtos);

            return Ok(new { message = $"Synced {concreteDtos.Count} orders" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing orders");
            return StatusCode(500, new { message = "Error syncing orders", error = ex.Message });
        }
    }

    [HttpPut("{orderId}/complete")]
    public async Task<IActionResult> ToggleOrderCompletion(int orderId, [FromBody] bool isCompleted)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            return NotFound();
        }

        order.IsCompleted = isCompleted;
        await _orderRepository.UpdateAsync(order);

        return Ok(order);
    }

    [HttpPut("items/{itemId}/prepare")]
    public async Task<IActionResult> ToggleItemPreparation(int itemId, [FromBody] bool isPrepared)
    {
        // We need to access OrderItems directly. 
        // Since we don't have an OrderItemRepository, we'll use DbContext directly or add method to OrderRepository
        // For now, let's use DbContext as it's available (though not ideal pattern, but pragmatic)
        // Better: Add UpdateItemAsync to IOrderRepository? No, repository is for Aggregate Root.
        // But OrderItem is part of Order aggregate.

        // Let's fetch the order containing the item
        var dbContext = _context as Microsoft.EntityFrameworkCore.DbContext;
        var item = await dbContext!.Set<OrderItem>().FindAsync(itemId);

        if (item == null)
        {
            return NotFound();
        }

        item.IsPrepared = isPrepared;
        await dbContext.SaveChangesAsync();

        return Ok(item);
    }
}

public class SyncOrdersRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
