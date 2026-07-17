using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class FetchRepositoryOperation : IRepositoryOperation
{
    private readonly IGitCommandExecutor _executor;

    public FetchRepositoryOperation(IGitCommandExecutor executor)
    {
        _executor = executor;
    }

    public string Key => "fetch";

    public string DisplayName => "Fetch";

    public Task<OperationResult> ExecuteAsync(
        string repositoryPath,
        RepositoryOperationOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "fetch", "--all", "--prune", "--progress" };
        if (options.IncludeSubmodules)
        {
            arguments.Add("--recurse-submodules=yes");
        }

        progress?.Report(options.IncludeSubmodules
            ? "Fetching the repository and its submodules…"
            : "Fetching all remotes…");

        return _executor.RunRepositoryCommandAsync(
            repositoryPath,
            "fetch",
            arguments,
            progress,
            cancellationToken);
    }
}
