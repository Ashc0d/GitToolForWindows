namespace GitTool.Core.Models;

public sealed record RecentRepositoryEntry(
    string Path,
    DateTimeOffset LastOpenedUtc);
