using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GourmetClientApp.Notifications;
using GourmetClient.Serialization;
using GourmetClientApp.Utils;

namespace GourmetClientApp.Settings;

public class GourmetSettingsService
{
    private readonly NotificationService _notificationService;
    private readonly string _settingsFileName;

    private GourmetClientSettings? _currentSettings;

    public event EventHandler? SettingsSaved;

    public GourmetSettingsService()
    {
        _notificationService = InstanceProvider.NotificationService;
        _settingsFileName = Path.Combine(App.LocalAppDataPath, "GourmetClientSettings.json");
    }

    public UserSettings GetCurrentUserSettings()
    {
        return GetCurrentSettings().UserSettings;
    }

    public void SaveUserSettings(UserSettings userSettings)
    {
        GourmetClientSettings settings = GetCurrentSettings();
        settings.UserSettings = userSettings;

        SaveSettings(settings);
    }

    public WindowSettings? GetCurrentWindowSettings()
    {
        return GetCurrentSettings().WindowSettings;
    }

    public void SaveWindowSettings(WindowSettings windowSettings)
    {
        GourmetClientSettings settings = GetCurrentSettings();
        settings.WindowSettings = windowSettings;

        SaveSettings(settings);
    }

    public UpdateSettings GetCurrentUpdateSettings()
    {
        return GetCurrentSettings().UpdateSettings;
    }

    public void SaveUpdateSettings(UpdateSettings updateSettings)
    {
        GourmetClientSettings settings = GetCurrentSettings();
        settings.UpdateSettings = updateSettings;

        SaveSettings(settings);
    }

    private GourmetClientSettings GetCurrentSettings()
    {
        return _currentSettings ??= ReadSettingsFromFile();
    }

    private GourmetClientSettings ReadSettingsFromFile()
    {
        if (!File.Exists(_settingsFileName))
        {
            return new GourmetClientSettings();
        }

        SerializableGourmetClientSettings? serializedSettings = null;
        GourmetClientSettings? settings = null;

        try
        {
            using var fileStream = new FileStream(_settingsFileName, FileMode.Open, FileAccess.Read, FileShare.None);
            serializedSettings = JsonSerializer.Deserialize<SerializableGourmetClientSettings>(fileStream);
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            // Loading the settings failed. Default settings will be used.
            _notificationService.Send(new ExceptionNotification("Laden der Einstellungen ist fehlgeschlagen.", exception));
        }

        try
        {
            settings = serializedSettings?.ToGourmetSettings();
        }
        catch (InvalidOperationException)
        {
            // Settings could not be converted to model. Use default settings instead.
        }

        return settings ?? new GourmetClientSettings();
    }

    private void SaveSettings(GourmetClientSettings settings)
    {
        SerializableGourmetClientSettings serializedSettings = SerializableGourmetClientSettings.FromGourmetClientSettings(settings);

        try
        {
            string? settingsDirectory = Path.GetDirectoryName(_settingsFileName);
            Debug.Assert(settingsDirectory is not null);

            Directory.CreateDirectory(settingsDirectory);

            using var fileStream = new FileStream(_settingsFileName, FileMode.Create, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(fileStream, serializedSettings, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (IOException exception)
        {
            // Saving the settings failed. Default settings will be used on next application start.
            _notificationService.Send(new ExceptionNotification("Speichern der Einstellungen ist fehlgeschlagen.", exception));
        }

        // This event triggers some updates, even if saving the settings to the file has failed. Since the settings are still available in
        // memory in this case, they can be used until the application shuts down.
        SettingsSaved?.Invoke(this, EventArgs.Empty);
    }
}