namespace GitTool.Core.Infrastructure;

public sealed class AppPaths
{
    public AppPaths()
    {
        DataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitTool");
        LogsRoot = Path.Combine(DataRoot, "Logs");
        SettingsFile = Path.Combine(DataRoot, "settings.json");
    }

    public string DataRoot { get; }

    public string LogsRoot { get; }

    public string SettingsFile { get; }
}
