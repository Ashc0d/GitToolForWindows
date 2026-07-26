namespace GitTool.Core.Models;

public enum CommitSignatureStatus
{
    Unknown,
    Unsigned,
    Signed
}

public sealed record GitCommitInfo(
    string Hash,
    string ShortHash,
    IReadOnlyList<string> ParentHashes,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    IReadOnlyList<string> References,
    string Subject,
    CommitSignatureStatus SignatureStatus = CommitSignatureStatus.Unknown)
{
    public bool IsHead => References.Any(reference =>
        reference.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("HEAD -> ", StringComparison.OrdinalIgnoreCase));

    public bool IsSigned => SignatureStatus == CommitSignatureStatus.Signed;
}

public sealed record GitHistoryResult(
    bool IsSuccess,
    IReadOnlyList<GitCommitInfo> Commits,
    string ErrorMessage = "",
    string Diagnostics = "",
    bool IsCancelled = false);

public sealed record GitCommitFileChange(
    string Path,
    int? Additions,
    int? Deletions);

public sealed record GitCommitDetails(
    GitCommitInfo Commit,
    string Message,
    IReadOnlyList<GitCommitFileChange> Files,
    int TotalAdditions,
    int TotalDeletions);

public sealed record GitCommitDetailsResult(
    bool IsSuccess,
    GitCommitDetails? Details,
    string ErrorMessage = "",
    string Diagnostics = "",
    bool IsCancelled = false);

public sealed record GitHistoryGraphRow(
    GitCommitInfo Commit,
    int NodeLane,
    bool HasIncomingEdge,
    IReadOnlyList<int> ContinuingLanes,
    IReadOnlyList<int> ParentLanes,
    int LaneCount,
    double GraphWidth);
