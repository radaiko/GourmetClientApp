using System.Threading.Tasks;
using System.Windows.Input;
using GourmetClient.Behaviors;
using GourmetClientApp.Notifications;
using GourmetClientApp.Settings;
using GourmetClientApp.Update;
using GourmetClientApp.Utils;

namespace GourmetClientApp.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly GourmetSettingsService _settingsService;
    private readonly UpdateService _updateService;
    private readonly NotificationService _notificationService;

    private string _loginUsername;
    private string _loginPassword;
    private string _ventopayUsername;
    private string _ventopayPassword;
    private bool _checkForUpdates;
    private bool _canJoinNextPreReleaseVersion;

    public SettingsViewModel()
    {
        _settingsService = InstanceProvider.SettingsService;
        _updateService = InstanceProvider.UpdateService;
        _notificationService = InstanceProvider.NotificationService;

        _loginUsername = string.Empty;
        _loginPassword = string.Empty;
        _ventopayUsername = string.Empty;
        _ventopayPassword = string.Empty;

        SaveSettingsCommand = new AsyncDelegateCommand(SaveSettings);
        JoinNextPreReleaseVersionCommand = new AsyncDelegateCommand(JoinNextPreReleaseVersion, () => _canJoinNextPreReleaseVersion);
    }

    public ICommand SaveSettingsCommand { get; }

    public ICommand JoinNextPreReleaseVersionCommand { get; }

    public string LoginUsername
    {
        get => _loginUsername;
        set
        {
            if (_loginUsername != value)
            {
                _loginUsername = value;
                OnPropertyChanged();
            }
        }
    }

    public string LoginPassword
    {
        private get => _loginPassword;
        set
        {
            if (_loginPassword != value)
            {
                _loginPassword = value;
                OnPropertyChanged();
            }
        }
    }

    public string VentopayUsername
    {
        get => _ventopayUsername;
        set
        {
            if (_ventopayUsername != value)
            {
                _ventopayUsername = value;
                OnPropertyChanged();
            }
        }
    }

    public string VentopayPassword
    {
        private get => _ventopayPassword;
        set
        {
            if (_ventopayPassword != value)
            {
                _ventopayPassword = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set
        {
            if (_checkForUpdates != value)
            {
                _checkForUpdates = value;
                OnPropertyChanged();
            }
        }
    }

    public override void Initialize()
    {
        UserSettings userSettings = _settingsService.GetCurrentUserSettings();
        UpdateSettings updateSettings = _settingsService.GetCurrentUpdateSettings();

        LoginUsername = userSettings.GourmetLoginUsername;
        LoginPassword = userSettings.GourmetLoginPassword;
        VentopayUsername = userSettings.VentopayUsername;
        VentopayPassword = userSettings.VentopayPassword;

        CheckForUpdates = updateSettings.CheckForUpdates;

        if (updateSettings.CheckForUpdates)
        {
            _updateService.CanJoinNextPreReleaseVersion().ContinueWith(OnCanJoinNextPreReleaseVersionTaskFinished);
        }
    }

    private Task SaveSettings()
    {
        UserSettings userSettings = _settingsService.GetCurrentUserSettings();
        UpdateSettings updateSettings = _settingsService.GetCurrentUpdateSettings();

        userSettings.GourmetLoginUsername = LoginUsername;
        userSettings.GourmetLoginPassword = LoginPassword;
        userSettings.VentopayUsername = VentopayUsername;
        userSettings.VentopayPassword = VentopayPassword;

        _settingsService.SaveUserSettings(userSettings);

        if (updateSettings.CheckForUpdates != CheckForUpdates)
        {
            updateSettings.CheckForUpdates = CheckForUpdates;
            _settingsService.SaveUpdateSettings(updateSettings);
        }

        return Task.CompletedTask;
    }

    private void OnCanJoinNextPreReleaseVersionTaskFinished(Task<bool> task)
    {
        if (task.IsCanceled)
        {
            return;
        }

        if (task.IsFaulted)
        {
            _notificationService.Send(new ExceptionNotification("Fehler beim Prüfen auf Vorab-Version", task.Exception));
            return;
        }

        _canJoinNextPreReleaseVersion = task.Result;
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task JoinNextPreReleaseVersion()
    {
        ReleaseDescription? preReleaseDescription = await _updateService.CheckForUpdate(true);
        if (preReleaseDescription is null)
        {
            _notificationService.Send(new Notification(NotificationType.Error, "Vorab-Version ist nicht mehr verfügbar"));
            return;
        }

        UpdateHelper.StartUpdate(preReleaseDescription);
    }
}