namespace GourmetClientApp.Settings;

public record UpdateSettings
{
    public bool CheckForUpdates { get; set; } = true;
}