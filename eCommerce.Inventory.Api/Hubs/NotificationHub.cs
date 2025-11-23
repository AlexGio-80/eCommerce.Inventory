using Microsoft.AspNetCore.SignalR;

namespace eCommerce.Inventory.Api.Hubs;

public class NotificationHub : Hub
{
    // Methods can be added here for client-to-server communication if needed.
    // For now, we primarily use IHubContext to send messages from server to clients.

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
