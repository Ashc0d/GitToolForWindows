namespace GitTool.Core.Models;

public enum OperationState
{
    Idle,
    Running,
    Cancelling,
    Cancelled,
    Completed,
    Failed
}

public sealed record OperationSnapshot(
    OperationState State,
    string Title,
    string Detail,
    DateTimeOffset UpdatedAt)
{
    public bool IsBusy => State is OperationState.Running or OperationState.Cancelling;

    public static OperationSnapshot Idle { get; } =
        new(OperationState.Idle, "Ready", "No Git operation is running.", DateTimeOffset.Now);
}
