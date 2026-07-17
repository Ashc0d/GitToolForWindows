using GitTool.Core.Models;

namespace GitTool.Core.Git;

public interface IGitCommandExecutor
{
    Task<OperationResult> RunRepositoryCommandAsync(
        string repositoryPath,
        string operationName,
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
