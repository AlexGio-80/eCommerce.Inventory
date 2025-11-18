using MediatR;

namespace eCommerce.Inventory.Application.Commands;

/// <summary>
/// CQRS Command to process a Card Trader webhook event
/// Handles order creation, updates, and deletion
/// Following SPECIFICATIONS: CQRS pattern, async operations, error handling
/// </summary>
public class ProcessCardTraderWebhookCommand : IRequest<Unit>
{
    public ProcessCardTraderWebhookCommand(
        string webhookId,
        string cause,
        int objectId,
        string mode,
        object data)
    {
        WebhookId = webhookId;
        Cause = cause; // "order.create", "order.update", "order.destroy"
        ObjectId = objectId;
        Mode = mode; // "test" or "live"
        Data = data; // Card Trader Order DTO or null
    }

    public string WebhookId { get; set; }
    public string Cause { get; set; }
    public int ObjectId { get; set; }
    public string Mode { get; set; }
    public object Data { get; set; }
}
