using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Application.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        [FromQuery] DateTime? to = null,
        [FromQuery] bool excludeNullDates = true)
    {
        _logger.LogInformation("Getting orders with filters - from: {From}, to: {To}, excludeNullDates: {ExcludeNullDates}", from, to, excludeNullDates);

        var orders = await _orderRepository.GetOrdersWithItemsAsync(from, to, excludeNullDates);

        _logger.LogInformation("Retrieved {Count} orders", orders.Count());

        return Ok(orders);
    }

    [HttpGet("unprepared-items")]
    public async Task<ActionResult<IEnumerable<UnpreparedItemDto>>> GetUnpreparedItems(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting unprepared items");
        var items = await _orderRepository.GetUnpreparedItemsAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncOrders([FromBody] SyncOrdersRequest request)
    {
        try
        {
            var from = request?.From;
            var to = request?.To;

            _logger.LogInformation("Manual sync of orders triggered");
            _logger.LogInformation("Received parameters - from: {From}, to: {To}", from, to);

            var orderDtos = await _cardTraderApiService.GetOrdersAsync(from, to);

            // Cast dynamic list to concrete DTO list
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
