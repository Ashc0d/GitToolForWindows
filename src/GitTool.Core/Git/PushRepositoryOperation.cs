using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class PushRepositoryOperation : IRepositoryOperation
{
    private readonly IGitCommandExecutor _executor;

    public PushRepositoryOperation(IGitCommandExecutor executor)
    {
        _executor = executor;
    }

    public string Key => "push";

    public string DisplayName => "Push";

    public Task<OperationResult> ExecuteAsync(
        string repositoryPath,
        RepositoryOperationOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Pushing committed changes to the configured upstream…");
        return _executor.RunRepositoryCommandAsync(
            repositoryPath,
            "push",
            ["push", "--progress"],
            progress,
            cancellationToken);
    }
}
