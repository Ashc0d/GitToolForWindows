using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GitTool.Core.Models;

namespace GitTool.Core.Infrastructure;

public sealed class ProcessRunner
{
    private const int MaximumCapturedCharacters = 96_000;

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(fileName, arguments, workingDirectory),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(false, -1, "", "", StartError: $"Could not start {fileName}.");
            }
        }
        catch (Win32Exception exception)
        {
            return new ProcessRunResult(false, -1, "", "", StartError: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new ProcessRunResult(false, -1, "", "", StartError: exception.Message);
        }

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var outputTask = PumpOutputAsync(process.StandardOutput, standardOutput, progress);
        var errorTask = PumpOutputAsync(process.StandardError, standardError, progress);
        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        var timedOut = false;

        if (timeout is null)
        {
            await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var delayTask = Task.Delay(timeout.Value, cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);
            if (completedTask != waitTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                timedOut = true;
                TryKillProcessTree(process);
                await waitTask.ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

        return new ProcessRunResult(
            true,
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString(),
            timedOut);
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["GIT_PROGRESS_DELAY"] = "0";
        return startInfo;
    }

    private static async Task PumpOutputAsync(
        StreamReader reader,
        StringBuilder destination,
        IProgress<string>? progress)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (destination.Length < MaximumCapturedCharacters)
            {
                destination.AppendLine(line);
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report(line.Trim());
            }
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // The process may have exited between the checks.
        }
    }
}
