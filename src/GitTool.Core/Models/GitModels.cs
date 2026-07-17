namespace GitTool.Core.Models;

public enum GitTransport
{
    Https,
    Ssh,
    Original
}

public sealed record ResolvedRepositoryUrl(
    string CloneUrl,
    string RepositoryName,
    GitTransport Transport);

public sealed record GitCloneRequest(
    string RepositoryUrl,
    string DestinationRoot,
    bool InitializeSubmodules);

public sealed record RepositoryOperationOptions(bool IncludeSubmodules);

public sealed record GitRepositoryInfo(
    string RootPath,
    string Branch,
    string RemoteUrl,
    int ChangedFileCount)
{
    public bool IsClean => ChangedFileCount == 0;
}

public sealed record RepositoryInspectionResult(
    bool IsGitRepository,
    GitRepositoryInfo? Repository,
    string ErrorMessage,
    string Diagnostics = "");
