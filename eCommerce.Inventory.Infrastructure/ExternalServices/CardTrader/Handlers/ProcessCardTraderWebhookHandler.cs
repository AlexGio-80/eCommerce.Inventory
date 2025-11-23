using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Handlers;

/// <summary>
/// Handler for Card Trader webhook commands
/// Processes order.create, order.update, order.destroy events
/// Following SPECIFICATIONS: Single Responsibility, Error Handling, Logging
/// </summary>
public class ProcessCardTraderWebhookHandler : IRequestHandler<ProcessCardTraderWebhookCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly InventorySyncService _syncService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ProcessCardTraderWebhookHandler> _logger;

    public ProcessCardTraderWebhookHandler(
        IApplicationDbContext context,
        InventorySyncService syncService,
        INotificationService notificationService,
        ILogger<ProcessCardTraderWebhookHandler> logger)
    {
        _context = context;
        _syncService = syncService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Unit> Handle(ProcessCardTraderWebhookCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Processing Card Trader webhook - ID: {WebhookId}, Cause: {Cause}, ObjectId: {ObjectId}, Mode: {Mode}",
                request.WebhookId, request.Cause, request.ObjectId, request.Mode);

            switch (request.Cause)
            {
                case "order.create":
                    await HandleOrderCreatedAsync(request, cancellationToken);
                    break;

                case "order.update":
                    await HandleOrderUpdatedAsync(request, cancellationToken);
                    break;

                case "order.destroy":
                    await HandleOrderDestroyedAsync(request, cancellationToken);
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown webhook cause: {Cause} for webhook {WebhookId}",
                        request.Cause, request.WebhookId);
                    break;
            }

            _logger.LogInformation("Webhook {WebhookId} processed successfully", request.WebhookId);
            return Unit.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing webhook {WebhookId} with cause {Cause}",
                request.WebhookId, request.Cause);
            throw;
        }
    }

    /// <summary>
    /// Handle order creation from Card Trader
    /// </summary>
    private async Task HandleOrderCreatedAsync(ProcessCardTraderWebhookCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Handling order creation webhook for order {OrderId}", request.ObjectId);

            // Cast data to CardTraderOrderDto
            if (request.Data is not CardTraderOrderDto orderDto)
            {
                _logger.LogWarning("Order data is null or invalid for webhook {WebhookId}", request.WebhookId);
                return;
            }

            // Sync the order to database
            var orderDtos = new List<CardTraderOrderDto> { orderDto };
            await _syncService.SyncOrdersAsync(orderDtos, cancellationToken);

            // Notify frontend
            await _notificationService.NotifyAsync("OrderCreated", orderDto);

            _logger.LogInformation("Order {OrderId} created successfully from webhook", request.ObjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order creation for webhook {WebhookId}", request.WebhookId);
            throw;
        }
    }

    /// <summary>
    /// Handle order update from Card Trader
    /// </summary>
    private async Task HandleOrderUpdatedAsync(ProcessCardTraderWebhookCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Handling order update webhook for order {OrderId}", request.ObjectId);

            // Cast data to CardTraderOrderDto
            if (request.Data is not CardTraderOrderDto orderDto)
            {
                _logger.LogWarning("Order data is null or invalid for webhook {WebhookId}", request.WebhookId);
                return;
            }

            // Sync the order update to database
            var orderDtos = new List<CardTraderOrderDto> { orderDto };
            await _syncService.SyncOrdersAsync(orderDtos, cancellationToken);

            // Notify frontend
            await _notificationService.NotifyAsync("OrderUpdated", orderDto);

            _logger.LogInformation("Order {OrderId} updated successfully from webhook", request.ObjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order update for webhook {WebhookId}", request.WebhookId);
            throw;
        }
    }

    /// <summary>
    /// Handle order deletion from Card Trader
    /// For now, we log this but don't delete from our database (data retention)
    /// </summary>
    private async Task HandleOrderDestroyedAsync(ProcessCardTraderWebhookCommand request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Handling order destruction webhook for order {OrderId}", request.ObjectId);

            var dbContext = _context as DbContext;
            var order = await dbContext!.Set<Domain.Entities.Order>()
                .FirstOrDefaultAsync(o => o.CardTraderOrderId == request.ObjectId, cancellationToken);

            if (order == null)
            {
                _logger.LogWarning("Order {OrderId} not found in database for deletion webhook", request.ObjectId);
                return;
            }

            // Option 1: Mark as deleted (soft delete pattern)
            // Option 2: Log and don't delete (data retention)
            // For now, we'll just log the deletion event and leave the data intact

            _logger.LogInformation(
                "Order {OrderId} marked for deletion in Card Trader (webhook received). " +
                "Local record kept for audit purposes",
                request.ObjectId);

            await Task.CompletedTask; // Placeholder for actual deletion logic if needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order destruction for webhook {WebhookId}", request.WebhookId);
            throw;
        }
    }
}
