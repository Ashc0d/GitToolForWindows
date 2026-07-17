namespace GitTool.Core.Models;

public sealed class AppSettings
{
    public string DefaultCloneDirectory { get; set; } = GetDefaultCloneDirectory();

    public bool NotificationsEnabled { get; set; } = true;

    public static AppSettings CreateDefault() => new();

    private static string GetDefaultCloneDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var root = string.IsNullOrWhiteSpace(documents)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : documents;

        return Path.Combine(root, "Repositories");
    }
}
