namespace GitTool.Core.Models;

public sealed record ProcessRunResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    string StartError = "")
{
    public bool IsSuccess => Started && !TimedOut && ExitCode == 0;
}
