namespace GourmetClientApp.Settings;

public class GourmetClientSettings
{
    public GourmetClientSettings()
    {
        UserSettings = new UserSettings();
        UpdateSettings = new UpdateSettings();
    }

    public UserSettings UserSettings { get; set; }

    public WindowSettings? WindowSettings { get; set; }

    public UpdateSettings UpdateSettings { get; set; }
}