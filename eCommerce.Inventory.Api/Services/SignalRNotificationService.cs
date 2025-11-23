using eCommerce.Inventory.Api.Hubs;
using eCommerce.Inventory.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace eCommerce.Inventory.Api.Services;

public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyAsync(string eventName, object data)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(eventName, data);
            _logger.LogInformation("Sent SignalR notification: {EventName}", eventName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR notification: {EventName}", eventName);
            // We don't throw here to avoid failing the webhook processing just because notification failed
        }
    }
}
