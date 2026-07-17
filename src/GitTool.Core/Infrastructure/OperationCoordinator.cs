using GitTool.Core.Models;

namespace GitTool.Core.Infrastructure;

public sealed class OperationCoordinator
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private readonly object _statusChangeLock = new();
    private OperationSnapshot _current = OperationSnapshot.Idle;
    private CancellationTokenSource? _activeCancellation;

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
        bool lockTaken;
        try
        {
            lockTaken = await _operationLock.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled();
        }

        if (!lockTaken)
        {
            return OperationResult.Failure(
                "Another Git operation is already running.",
                "Wait for the current operation to finish before starting another one.");
        }

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            SetActiveOperation(operationCancellation, new OperationSnapshot(
                OperationState.Running,
                title,
                "Preparing…",
                DateTimeOffset.Now));

            var progress = new Progress<string>(detail =>
            {
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    UpdateRunningProgress(operationCancellation, title, detail);
                }
            });

            OperationResult result;
            try
            {
                result = await operation(progress, operationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
            {
                result = OperationResult.Cancelled();
            }
            catch (Exception exception)
            {
                result = OperationResult.Failure(
                    "The operation stopped because of an unexpected error.",
                    exception.ToString());
            }

            if (operationCancellation.IsCancellationRequested && !result.IsCancelled)
            {
                result = OperationResult.Cancelled(
                    standardError: result.StandardError,
                    standardOutput: result.StandardOutput,
                    exitCode: result.ExitCode);
            }

            var finalState = result.IsCancelled
                ? OperationState.Cancelled
                : result.IsSuccess
                    ? OperationState.Completed
                    : OperationState.Failed;

            CompleteOperation(operationCancellation, new OperationSnapshot(
                finalState,
                result.IsCancelled ? "Operation cancelled" : title,
                result.Summary,
                DateTimeOffset.Now));

            return result;
        }
        finally
        {
            ClearActiveOperation(operationCancellation);
            _operationLock.Release();
        }
    }

    public bool CancelCurrentOperation()
    {
        CancellationTokenSource cancellation;
        OperationSnapshot snapshot;

        lock (_statusChangeLock)
        {
            lock (_snapshotLock)
            {
                if (_activeCancellation is null || _current.State != OperationState.Running)
                {
                    return false;
                }

                cancellation = _activeCancellation;
                snapshot = new OperationSnapshot(
                    OperationState.Cancelling,
                    _current.Title,
                    "Cancelling… waiting for Git to stop safely.",
                    DateTimeOffset.Now);
                _current = snapshot;
            }

            StatusChanged?.Invoke(this, snapshot);
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The operation completed between publishing Cancelling and signalling it.
            return false;
        }
        catch (AggregateException)
        {
            // Cancellation is still requested even if an operation registered a faulty callback.
            return true;
        }
    }

    private void SetActiveOperation(
        CancellationTokenSource cancellation,
        OperationSnapshot snapshot)
    {
        lock (_statusChangeLock)
        {
            lock (_snapshotLock)
            {
                _activeCancellation = cancellation;
                _current = snapshot;
            }

            StatusChanged?.Invoke(this, snapshot);
        }
    }

    private void UpdateRunningProgress(
        CancellationTokenSource cancellation,
        string title,
        string detail)
    {
        OperationSnapshot snapshot;

        lock (_statusChangeLock)
        {
            lock (_snapshotLock)
            {
                if (!ReferenceEquals(_activeCancellation, cancellation)
                    || _current.State != OperationState.Running)
                {
                    return;
                }

                snapshot = new OperationSnapshot(
                    OperationState.Running,
                    title,
                    detail,
                    DateTimeOffset.Now);
                _current = snapshot;
            }

            StatusChanged?.Invoke(this, snapshot);
        }
    }

    private void CompleteOperation(
        CancellationTokenSource cancellation,
        OperationSnapshot snapshot)
    {
        lock (_statusChangeLock)
        {
            lock (_snapshotLock)
            {
                if (ReferenceEquals(_activeCancellation, cancellation))
                {
                    _activeCancellation = null;
                }

                _current = snapshot;
            }

            StatusChanged?.Invoke(this, snapshot);
        }
    }

    private void ClearActiveOperation(CancellationTokenSource cancellation)
    {
        lock (_snapshotLock)
        {
            if (ReferenceEquals(_activeCancellation, cancellation))
            {
                _activeCancellation = null;
            }
        }
    }
}
