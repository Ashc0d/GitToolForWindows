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
        TimeSpan? timeout = null,
        string? standardInput = null,
        int maximumCapturedCharacters = MaximumCapturedCharacters)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ProcessRunResult(false, -1, "", "", IsCancelled: true);
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(
                fileName,
                arguments,
                workingDirectory,
                redirectStandardInput: standardInput is not null),
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
        var inputTask = standardInput is null
            ? Task.CompletedTask
            : WriteStandardInputAsync(process, standardInput);
        var outputTask = PumpOutputAsync(
            process.StandardOutput,
            standardOutput,
            progress,
            maximumCapturedCharacters);
        var errorTask = PumpOutputAsync(
            process.StandardError,
            standardError,
            progress,
            maximumCapturedCharacters);
        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationSignal);
        var timeoutTask = timeout is null
            ? Task.Delay(Timeout.InfiniteTimeSpan)
            : Task.Delay(timeout.Value);

        var completedTask = await Task.WhenAny(
                waitTask,
                cancellationSignal.Task,
                timeoutTask)
            .ConfigureAwait(false);

        var timedOut = false;
        var cancelled = false;
        string? shutdownError = null;

        if (completedTask == cancellationSignal.Task)
        {
            cancelled = true;
            shutdownError = TryKillProcessTree(process);
            await waitTask.ConfigureAwait(false);
        }
        else if (completedTask == timeoutTask)
        {
            timedOut = true;
            shutdownError = TryKillProcessTree(process);
            await waitTask.ConfigureAwait(false);
        }

        await Task.WhenAll(inputTask, outputTask, errorTask).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(shutdownError))
        {
            standardError.AppendLine(shutdownError);
        }

        return new ProcessRunResult(
            true,
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString(),
            timedOut,
            cancelled);
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        bool redirectStandardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStandardInput,
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
        IProgress<string>? progress,
        int maximumCapturedCharacters)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (destination.Length < maximumCapturedCharacters)
            {
                destination.AppendLine(line);
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report(line.Trim());
            }
        }
    }

    private static async Task WriteStandardInputAsync(
        Process process,
        string standardInput)
    {
        try
        {
            await process.StandardInput.WriteAsync(standardInput)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or ObjectDisposedException)
        {
            // Process exit, timeout, or cancellation can close the input pipe.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (Exception exception) when (
                exception is IOException
                    or InvalidOperationException
                    or ObjectDisposedException)
            {
                // The process has already closed its input pipe.
            }
        }
    }

    private static string? TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            return null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            try
            {
                if (process.HasExited)
                {
                    return null;
                }
            }
            catch
            {
                // Preserve the original shutdown error below.
            }

            return $"The process tree could not be stopped: {exception.Message}";
        }
    }
}
