using System.Collections.ObjectModel;

namespace GourmetClientApp.Notifications;

public class NotificationService
{
    private readonly ObservableCollection<Notification> _notifications;

    public NotificationService()
    {
        _notifications = [];
        Notifications = new ReadOnlyObservableCollection<Notification>(_notifications);
    }

    public ReadOnlyObservableCollection<Notification> Notifications { get; }

    public void Send(Notification notification)
    {
        _notifications.Add(notification);
    }

    public void Dismiss(Notification notification)
    {
        _notifications.Remove(notification);
    }
}