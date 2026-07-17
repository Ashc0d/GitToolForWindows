using GitTool.Core.Models;

namespace GitTool.Core.Infrastructure;

public sealed class OperationCoordinator
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private OperationSnapshot _current = OperationSnapshot.Idle;

    public event EventHandler<OperationSnapshot>? StatusChanged;

    public OperationSnapshot Current
    {
        get
        {
            lock (_snapshotLock)
            {
                return _current;
            }
        }
    }

    public async Task<OperationResult> ExecuteAsync(
        string title,
        Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult.Failure(
                "Another Git operation is already running.",
                "Wait for the current operation to finish before starting another one.");
        }

        try
        {
            Update(new OperationSnapshot(
                OperationState.Running,
                title,
                "Preparing…",
                DateTimeOffset.Now));

            var progress = new Progress<string>(detail =>
            {
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    Update(new OperationSnapshot(
                        OperationState.Running,
                        title,
                        detail,
                        DateTimeOffset.Now));
                }
            });

            OperationResult result;
            try
            {
                result = await operation(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = OperationResult.Failure("The operation was cancelled.");
            }
            catch (Exception exception)
            {
                result = OperationResult.Failure(
                    "The operation stopped because of an unexpected error.",
                    exception.ToString());
            }

            Update(new OperationSnapshot(
                result.IsSuccess ? OperationState.Completed : OperationState.Failed,
                title,
                result.Summary,
                DateTimeOffset.Now));

            return result;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void Update(OperationSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            _current = snapshot;
        }

        StatusChanged?.Invoke(this, snapshot);
    }
}
