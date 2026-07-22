namespace GitTool.Core.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? DefaultDataRoot
            : Path.GetFullPath(dataRoot);
        LogsRoot = Path.Combine(DataRoot, "Logs");
        SettingsFile = Path.Combine(DataRoot, "settings.json");
    }

    public static string DefaultDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GitTool");

    public string DataRoot { get; }

    public string LogsRoot { get; }

    public string SettingsFile { get; }
}
