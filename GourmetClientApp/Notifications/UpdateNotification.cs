using System;

namespace GourmetClientApp.Notifications;

public class UpdateNotification : Notification
{
    public UpdateNotification(string message, Action startUpdateCallback)
        : base(NotificationType.Information, message)
    {
        StartUpdateAction = startUpdateCallback;
    }

    public Action StartUpdateAction { get; }
}