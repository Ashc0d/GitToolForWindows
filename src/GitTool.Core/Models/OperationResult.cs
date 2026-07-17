namespace GitTool.Core.Models;

public sealed record OperationResult(
    bool IsSuccess,
    string Summary,
    string StandardOutput = "",
    string StandardError = "",
    int ExitCode = 0)
{
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
}
