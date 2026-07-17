using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class RepositoryOperationRegistry
{
    private readonly IReadOnlyDictionary<string, IRepositoryOperation> _operations;

    public RepositoryOperationRegistry(IEnumerable<IRepositoryOperation> operations)
    {
        _operations = operations.ToDictionary(
            operation => operation.Key,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IRepositoryOperation> Operations => _operations.Values.ToArray();

    public Task<OperationResult> ExecuteAsync(
        string key,
        string repositoryPath,
        RepositoryOperationOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!_operations.TryGetValue(key, out var operation))
        {
            return Task.FromResult(OperationResult.Failure(
                $"The repository operation '{key}' is not registered."));
        }

        return operation.ExecuteAsync(
            repositoryPath,
            options,
            progress,
            cancellationToken);
    }
}
