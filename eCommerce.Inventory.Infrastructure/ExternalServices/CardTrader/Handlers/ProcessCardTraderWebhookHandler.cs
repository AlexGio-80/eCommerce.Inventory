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
    private readonly IPriceRefreshQueue _priceRefreshQueue;
    private readonly ILogger<ProcessCardTraderWebhookHandler> _logger;

    public ProcessCardTraderWebhookHandler(
        IApplicationDbContext context,
        InventorySyncService syncService,
        INotificationService notificationService,
        IPriceRefreshQueue priceRefreshQueue,
        ILogger<ProcessCardTraderWebhookHandler> logger)
    {
        _context = context;
        _syncService = syncService;
        _notificationService = notificationService;
        _priceRefreshQueue = priceRefreshQueue;
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

            // Una vendita è essa stessa un segnale di mercato: la carta appena venduta
            // viene rimessa in coda per una rivalutazione immediata del prezzo.
            await EnqueueSoldCardsForRepricingAsync(orderDto, cancellationToken);

            _logger.LogInformation("Order {OrderId} created successfully from webhook", request.ObjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling order creation for webhook {WebhookId}", request.WebhookId);
            throw;
        }
    }

    /// <summary>
    /// Accoda per rivalutazione i blueprint delle carte appena vendute.
    ///
    /// Vengono considerate solo le carte di cui resta ancora una copia a magazzino:
    /// riprezzare qualcosa che non si ha più consumerebbe chiamate API senza alcun effetto.
    /// L'operazione non deve mai far fallire l'elaborazione del webhook, perché la
    /// registrazione dell'ordine è molto più importante dell'aggiornamento di prezzo.
    /// </summary>
    private async Task EnqueueSoldCardsForRepricingAsync(CardTraderOrderDto orderDto, CancellationToken cancellationToken)
    {
        try
        {
            var soldCardTraderBlueprintIds = orderDto.OrderItems?
                .Where(i => i.BlueprintId.HasValue)
                .Select(i => i.BlueprintId!.Value)
                .Distinct()
                .ToList() ?? new List<int>();

            if (soldCardTraderBlueprintIds.Count == 0) return;

            // Il webhook porta gli id Card Trader, la coda lavora con gli id locali.
            var localBlueprints = await _context.Blueprints
                .AsNoTracking()
                .Where(b => soldCardTraderBlueprintIds.Contains(b.CardTraderId))
                .Select(b => new { b.Id, b.CardTraderId })
                .ToListAsync(cancellationToken);

            if (localBlueprints.Count == 0)
            {
                _logger.LogDebug("Nessun blueprint locale corrispondente alle carte vendute: rivalutazione non accodata");
                return;
            }

            var localIds = localBlueprints.Select(b => b.Id).ToList();

            var stillInStock = await _context.InventoryItems
                .AsNoTracking()
                .Where(i => localIds.Contains(i.BlueprintId) && i.Quantity > 0)
                .Select(i => i.BlueprintId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var blueprintId in stillInStock)
            {
                _priceRefreshQueue.Enqueue(blueprintId, $"vendita ordine {orderDto.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Impossibile accodare la rivalutazione dei prezzi per l'ordine {OrderId}: l'ordine resta comunque registrato",
                orderDto.Id);
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
