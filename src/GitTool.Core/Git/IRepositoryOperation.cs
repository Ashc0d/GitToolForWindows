using GitTool.Core.Models;

namespace GitTool.Core.Git;

public interface IRepositoryOperation
{
    string Key { get; }

    string DisplayName { get; }

    Task<OperationResult> ExecuteAsync(
        string repositoryPath,
        RepositoryOperationOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
