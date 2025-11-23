namespace eCommerce.Inventory.Application.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(string eventName, object data);
}
