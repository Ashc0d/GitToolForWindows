using System.Globalization;
using GitTool.Core.Models;
using Microsoft.UI.Xaml;

namespace GitTool.App.Views;

internal sealed class GitHistoryRowViewModel(GitHistoryGraphRow graph)
{
    public GitHistoryGraphRow Graph { get; } = graph;

    public GitCommitInfo Commit => Graph.Commit;

    public string Subject => Commit.Subject;

    public string ShortHash => Commit.ShortHash;

    public string ReferencesText => string.Join("  •  ", Commit.References);

    public string AuthorSummary =>
        $"{Commit.AuthorName}  •  {Commit.AuthorDate.ToLocalTime():g}";

    public Visibility SignedBadgeVisibility =>
        Commit.SignatureStatus == CommitSignatureStatus.Signed
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility UnsignedBadgeVisibility =>
        Commit.SignatureStatus == CommitSignatureStatus.Unsigned
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility UnknownBadgeVisibility =>
        Commit.SignatureStatus == CommitSignatureStatus.Unknown
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string SignatureDescription => Commit.SignatureStatus switch
    {
        CommitSignatureStatus.Signed => "signed",
        CommitSignatureStatus.Unsigned => "unsigned",
        _ => "signature status unavailable"
    };

    public string AutomationName =>
        $"{Commit.Subject}, commit {Commit.ShortHash}, by {Commit.AuthorName}, "
        + SignatureDescription;
}

internal sealed class GitCommitFileViewModel(GitCommitFileChange change)
{
    public string Path { get; } = change.Path;

    public string AdditionsText { get; } = change.Additions is null
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"+{change.Additions}");

    public string DeletionsText { get; } = change.Deletions is null
        ? string.Empty
        : string.Create(CultureInfo.InvariantCulture, $"−{change.Deletions}");

    public string BinaryText { get; } =
        change.Additions is null || change.Deletions is null
            ? "Binary"
            : string.Empty;

    public Visibility TextChangesVisibility { get; } =
        change.Additions is not null && change.Deletions is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility BinaryVisibility { get; } =
        change.Additions is null || change.Deletions is null
            ? Visibility.Visible
            : Visibility.Collapsed;
}

internal sealed class RecentRepositoryViewModel(RecentRepositoryEntry entry)
{
    public string Path { get; } = entry.Path;

    public string Name { get; } =
        new DirectoryInfo(entry.Path).Name is { Length: > 0 } name
            ? name
            : entry.Path;

    public string LastOpenedText { get; } =
        $"Opened {entry.LastOpenedUtc.ToLocalTime():g}";

    public bool IsAvailable { get; } = Directory.Exists(entry.Path);

    public string AvailabilityText { get; } =
        Directory.Exists(entry.Path) ? string.Empty : "Folder not found";

    public Visibility MissingVisibility { get; } =
        Directory.Exists(entry.Path)
            ? Visibility.Collapsed
            : Visibility.Visible;
}
