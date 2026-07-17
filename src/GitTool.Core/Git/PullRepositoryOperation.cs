using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class PullRepositoryOperation : IRepositoryOperation
{
    private readonly IGitCommandExecutor _executor;

    public PullRepositoryOperation(IGitCommandExecutor executor)
    {
        _executor = executor;
    }

    public string Key => "pull";

    public string DisplayName => "Pull";

    public async Task<OperationResult> ExecuteAsync(
        string repositoryPath,
        RepositoryOperationOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("Pulling the current branch with fast-forward safety…");
        var pullResult = await _executor.RunRepositoryCommandAsync(
                repositoryPath,
                "pull",
                ["pull", "--ff-only", "--progress"],
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (!pullResult.IsSuccess || !options.IncludeSubmodules)
        {
            return pullResult;
        }

        progress?.Report("Updating and initializing submodules…");
        var submoduleResult = await _executor.RunRepositoryCommandAsync(
                repositoryPath,
                "submodule update",
                ["submodule", "update", "--init", "--recursive", "--progress"],
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        return submoduleResult.IsSuccess
            ? OperationResult.Success(
                "Pull and submodule update completed.",
                pullResult.StandardOutput + submoduleResult.StandardOutput)
            : submoduleResult;
    }
}
