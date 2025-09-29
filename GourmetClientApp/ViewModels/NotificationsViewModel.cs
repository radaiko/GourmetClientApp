using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using GourmetClientApp.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GourmetClientApp.ViewModels;

public class NotificationsViewModel : ObservableObject
{
    private readonly NotificationService _notificationService;

    public NotificationsViewModel()
    {
        _notificationService = InstanceProvider.NotificationService;

        DismissNotificationCommand = new AsyncDelegateCommand<Notification>(DismissNotification);
        StartUpdateCommand = new AsyncDelegateCommand<UpdateNotification>(StartUpdate, p => p?.StartUpdateAction is not null);
        ShowExceptionDetailsCommand = new AsyncDelegateCommand<ExceptionNotification>(ShowExceptionDetails, p => p?.Exception is not null);
    }

    public ICommand DismissNotificationCommand { get; }

    public ICommand StartUpdateCommand { get; }

    public ICommand ShowExceptionDetailsCommand { get; }

    public IReadOnlyList<Notification> Notifications => _notificationService.Notifications;

    private Task DismissNotification(Notification? notification)
    {
        if (notification is not null)
        {
            _notificationService.Dismiss(notification);
        }

        return Task.CompletedTask;
    }

    private Task ShowExceptionDetails(ExceptionNotification? notification)
    {
        if (notification is not null)
        {
            var window = new ExceptionNotificationDetailWindow
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Notification = notification
            };
            window.ShowDialog();
        }

        return Task.CompletedTask;
    }

    private Task StartUpdate(UpdateNotification? notification)
    {
        if (notification is not null)
        {
            notification.StartUpdateAction();
        }

        return Task.CompletedTask;
    }
}