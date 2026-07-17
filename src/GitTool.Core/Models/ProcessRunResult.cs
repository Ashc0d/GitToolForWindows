namespace GitTool.Core.Models;

public sealed record ProcessRunResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool IsCancelled = false,
    string StartError = "")
{
    public bool IsSuccess => Started && !TimedOut && !IsCancelled && ExitCode == 0;
}
