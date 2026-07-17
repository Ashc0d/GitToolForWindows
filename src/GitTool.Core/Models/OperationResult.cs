namespace GitTool.Core.Models;

public sealed record OperationResult(
    bool IsSuccess,
    string Summary,
    string StandardOutput = "",
    string StandardError = "",
    int ExitCode = 0,
    OperationCancellationMetadata? Cancellation = null)
{
    public bool IsCancelled => Cancellation is not null;

    public bool HasCancellationWarning => Cancellation?.CleanupFailed == true;

    public string Diagnostics
    {
        get
        {
            var details = string.Join(
                Environment.NewLine,
                new[] { StandardError.Trim(), StandardOutput.Trim() }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(details) ? Summary : details;
        }
    }

    public static OperationResult Success(string summary, string output = "") =>
        new(true, summary, output);

    public static OperationResult Failure(
        string summary,
        string standardError = "",
        string standardOutput = "",
        int exitCode = -1) =>
        new(false, summary, standardOutput, standardError, exitCode);

    public static OperationResult Cancelled(
        string summary = "The operation was cancelled.",
        string standardError = "",
        string standardOutput = "",
        int exitCode = -1,
        OperationCancellationMetadata? cancellation = null) =>
        new(
            false,
            summary,
            standardOutput,
            standardError,
            exitCode,
            cancellation ?? OperationCancellationMetadata.NoCleanup);
}

public sealed record OperationCancellationMetadata(
    bool CleanupAttempted,
    bool CleanupSucceeded,
    string? RemainingPath = null)
{
    public bool CleanupFailed => CleanupAttempted && !CleanupSucceeded;

    public static OperationCancellationMetadata NoCleanup { get; } = new(false, true);
}
