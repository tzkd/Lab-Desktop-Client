namespace LabDesktop.Client.App.Configuration;

internal sealed class AppPaths
{
    public AppPaths(string? localApplicationData = null)
    {
        var root = localApplicationData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        DataDirectory = Path.Combine(root, "Lab Desktop Client");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        LogFile = Path.Combine(LogDirectory, "client.log");
    }

    public string DataDirectory { get; }

    public string SettingsFile { get; }

    public string LogDirectory { get; }

    public string LogFile { get; }
}
