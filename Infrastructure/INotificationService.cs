namespace ArnoxPay.Infrastructure;

public interface INotificationService
{
    /// <summary>
    /// Saves an in-app notification and optionally sends email and/or SMS
    /// based on appsettings (NotificationSettings).
    /// </summary>
    Task CreateAsync(int customerId, string title, string message);
}
