using System.Collections.Concurrent;
using System.Diagnostics;
using GitTool.Core.Git;
using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

if (args.Length > 0)
{
    if (args[0] == "--process-tree-parent")
    {
        await RunProcessTreeParentAsync();
        return;
    }

    if (args[0] == "--process-tree-child")
    {
        Console.WriteLine("CHILD_READY");
        Console.Out.Flush();
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return;
    }

    if (args[0] is "clone" or "-C")
    {
        await RunGitShimAsync(args);
        return;
    }
}

var resolver = new GitUrlResolver();
var tests = new (string Input, bool PreferSsh, string ExpectedUrl, string ExpectedName, GitTransport Transport)[]
{
    ("https://github.com/microsoft/WinUI-Gallery", true, "git@github.com:microsoft/WinUI-Gallery.git", "WinUI-Gallery", GitTransport.Ssh),
    ("git@github.com:microsoft/WinUI-Gallery.git", false, "https://github.com/microsoft/WinUI-Gallery.git", "WinUI-Gallery", GitTransport.Https),
    ("microsoft/WinUI-Gallery", false, "https://github.com/microsoft/WinUI-Gallery.git", "WinUI-Gallery", GitTransport.Https),
    ("https://example.com/team/project.git", true, "https://example.com/team/project.git", "project", GitTransport.Https)
};

foreach (var test in tests)
{
    var actual = resolver.Resolve(test.Input, test.PreferSsh);
    AssertEqual(test.ExpectedUrl, actual.CloneUrl, $"URL for {test.Input}");
    AssertEqual(test.ExpectedName, actual.RepositoryName, $"name for {test.Input}");
    AssertEqual(test.Transport, actual.Transport, $"transport for {test.Input}");
}

await RunProcessCancellationTestsAsync();
await RunOperationCoordinatorCancellationTestsAsync();
await RunCloneCancellationCleanupTestsAsync();
await RunRepositoryCancellationPreservationTestsAsync();
await RunLocalRepositoryIntegrationTestsAsync();
Console.WriteLine(
    $"[OK] {tests.Length} URL cases, cancellation safety checks, clone cleanup checks, and local Git integration checks passed.");
return;

static void AssertEqual<T>(T expected, T actual, string subject)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"[FAIL] Expected {subject} to be '{expected}', but got '{actual}'.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"[FAIL] {message}");
    }
}

static async Task RunProcessCancellationTestsAsync()
{
    var runner = new ProcessRunner();
    var executable = GetTestExecutablePath();
    using var cancellation = new CancellationTokenSource();
    var childReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var progress = new CallbackProgress<string>(line =>
    {
        if (line.Equals("CHILD_READY", StringComparison.Ordinal))
        {
            childReady.TrySetResult(true);
        }
    });

    var runTask = runner.RunAsync(
        executable,
        ["--process-tree-parent"],
        Path.GetDirectoryName(executable),
        progress,
        cancellation.Token);

    await childReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var stopwatch = Stopwatch.StartNew();
    await cancellation.CancelAsync();
    var result = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    stopwatch.Stop();

    AssertTrue(result.IsCancelled, "Process cancellation was not reported explicitly.");
    AssertTrue(!result.TimedOut, "A deliberate process cancellation was reported as a timeout.");
    AssertTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "Process cancellation did not return promptly.");
    AssertTrue(
        result.StandardOutput.Contains("CHILD_READY", StringComparison.Ordinal),
        "Child-process output was not drained after cancellation.");

    var childIdLine = result.StandardOutput
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Single(line => line.StartsWith("CHILD_PID=", StringComparison.Ordinal));
    var childId = int.Parse(childIdLine["CHILD_PID=".Length..]);
    AssertTrue(HasProcessExited(childId), "Cancellation did not stop the child process tree.");

    var timeoutResult = await runner.RunAsync(
        executable,
        ["--process-tree-parent"],
        Path.GetDirectoryName(executable),
        null,
        CancellationToken.None,
        TimeSpan.FromMilliseconds(500));
    AssertTrue(timeoutResult.TimedOut, "A process timeout was not reported.");
    AssertTrue(!timeoutResult.IsCancelled, "A process timeout was incorrectly reported as cancellation.");
}

static async Task RunOperationCoordinatorCancellationTestsAsync()
{
    var coordinator = new OperationCoordinator();
    var states = new ConcurrentQueue<OperationState>();
    var operationStarted = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    coordinator.StatusChanged += (_, snapshot) => states.Enqueue(snapshot.State);

    var activeOperation = coordinator.ExecuteAsync(
        "Long-running operation",
        async (_, cancellationToken) =>
        {
            operationStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return OperationResult.Success("Unexpected completion.");
        });

    await operationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var duplicate = await coordinator.ExecuteAsync(
        "Duplicate operation",
        (_, _) => Task.FromResult(OperationResult.Success("Unexpected duplicate completion.")));
    AssertTrue(
        !duplicate.IsSuccess && !duplicate.IsCancelled,
        "The coordinator did not reject a duplicate operation.");

    AssertTrue(coordinator.CancelCurrentOperation(), "The coordinator rejected cancellation of its active task.");
    var cancelled = await activeOperation.WaitAsync(TimeSpan.FromSeconds(5));
    AssertTrue(cancelled.IsCancelled, "The coordinator did not return a cancelled result.");

    var observedStates = states.ToArray();
    AssertTrue(observedStates.Contains(OperationState.Running), "Running state was not published.");
    AssertTrue(observedStates.Contains(OperationState.Cancelling), "Cancelling state was not published.");
    AssertTrue(observedStates.Contains(OperationState.Cancelled), "Cancelled state was not published.");
    AssertTrue(
        Array.IndexOf(observedStates, OperationState.Running)
        < Array.IndexOf(observedStates, OperationState.Cancelling)
        && Array.IndexOf(observedStates, OperationState.Cancelling)
        < Array.IndexOf(observedStates, OperationState.Cancelled),
        "Cancellation state transitions were published out of order.");
    AssertTrue(!coordinator.CancelCurrentOperation(), "The coordinator retained a completed cancellation source.");

    var subsequent = await coordinator.ExecuteAsync(
        "Subsequent operation",
        (_, _) => Task.FromResult(OperationResult.Success("Subsequent operation completed.")));
    AssertTrue(subsequent.IsSuccess, "A subsequent operation could not run after cancellation.");
}

static async Task RunCloneCancellationCleanupTestsAsync()
{
    var testRoot = Path.Combine(Path.GetTempPath(), "GitTool.CloneCancellation", Guid.NewGuid().ToString("N"));
    var targetPath = Path.Combine(testRoot, "reusable");
    var originalMode = Environment.GetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE");
    Directory.CreateDirectory(testRoot);

    try
    {
        var runner = new ProcessRunner();
        var logger = new TestLogger();
        var client = new GitClient(
            runner,
            new GitUrlResolver(),
            new FixedSshProbe(false),
            logger,
            GetTestExecutablePath());
        var request = new GitCloneRequest(
            "https://example.invalid/owner/reusable.git",
            testRoot,
            false);

        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "slow");
        using (var cancellation = new CancellationTokenSource())
        {
            var cloneReady = CreateProgressSignal("CLONE_READY=");
            var cloneTask = client.CloneAsync(request, cloneReady.Progress, cancellation.Token);
            await cloneReady.Signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            var result = await cloneTask.WaitAsync(TimeSpan.FromSeconds(5));

            AssertTrue(result.IsCancelled, "A cancelled clone was not reported as cancelled.");
            AssertTrue(
                result.Cancellation is { CleanupAttempted: true, CleanupSucceeded: true },
                "A cancelled clone did not report successful cleanup.");
            AssertTrue(!Directory.Exists(targetPath), "The app-created partial clone directory was not removed.");
        }

        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "complete");
        var retry = await client.CloneAsync(request, null, CancellationToken.None);
        AssertTrue(retry.IsSuccess, "The clone destination could not be reused after cancellation cleanup.");
        AssertTrue(Directory.Exists(targetPath), "The retry clone did not create its target directory.");

        Directory.Delete(targetPath, true);
        Directory.CreateDirectory(targetPath);
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "slow");
        using (var cancellation = new CancellationTokenSource())
        {
            var cloneReady = CreateProgressSignal("CLONE_READY=");
            var cloneTask = client.CloneAsync(request, cloneReady.Progress, cancellation.Token);
            await cloneReady.Signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            var result = await cloneTask.WaitAsync(TimeSpan.FromSeconds(5));

            AssertTrue(result.IsCancelled, "Cancellation into a pre-existing target was not reported.");
            AssertTrue(
                result.Cancellation is { CleanupAttempted: false },
                "Cleanup was attempted against a pre-existing target.");
            AssertTrue(Directory.Exists(targetPath), "A pre-existing empty target directory was deleted.");
            AssertTrue(
                File.Exists(Path.Combine(targetPath, ".gittool-partial")),
                "Files written into the pre-existing target were unexpectedly removed.");
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", originalMode);
        TryDeleteTestDirectory(testRoot);
    }
}

static async Task RunRepositoryCancellationPreservationTestsAsync()
{
    var testRoot = Path.Combine(Path.GetTempPath(), "GitTool.RepositoryCancellation", Guid.NewGuid().ToString("N"));
    var sentinelPath = Path.Combine(testRoot, "user-owned.txt");
    var originalMode = Environment.GetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE");
    Directory.CreateDirectory(testRoot);
    await File.WriteAllTextAsync(sentinelPath, "preserve me");

    try
    {
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "slow");
        var executor = new GitCommandExecutor(
            new ProcessRunner(),
            new TestLogger(),
            GetTestExecutablePath());
        var registry = new RepositoryOperationRegistry(
        [
            new FetchRepositoryOperation(executor),
            new PullRepositoryOperation(executor),
            new PushRepositoryOperation(executor)
        ]);

        foreach (var operation in new[] { "fetch", "pull", "push" })
        {
            using var cancellation = new CancellationTokenSource();
            var operationReady = CreateProgressSignal($"REPOSITORY_OPERATION_READY={operation}");
            var operationTask = registry.ExecuteAsync(
                operation,
                testRoot,
                new RepositoryOperationOptions(false),
                operationReady.Progress,
                cancellation.Token);

            await operationReady.Signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            var result = await operationTask.WaitAsync(TimeSpan.FromSeconds(5));

            AssertTrue(result.IsCancelled, $"Git {operation} cancellation was not reported.");
            AssertTrue(File.Exists(sentinelPath), $"Git {operation} cancellation deleted repository files.");
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", originalMode);
        TryDeleteTestDirectory(testRoot);
    }
}

static async Task RunProcessTreeParentAsync()
{
    var startInfo = new ProcessStartInfo
    {
        FileName = GetTestExecutablePath(),
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true
    };
    startInfo.ArgumentList.Add("--process-tree-child");

    using var child = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start the process-tree test child.");
    var readyLine = await child.StandardOutput.ReadLineAsync()
        ?? throw new InvalidOperationException("The process-tree test child did not report readiness.");

    Console.WriteLine($"CHILD_PID={child.Id}");
    Console.WriteLine(readyLine);
    Console.Out.Flush();
    await Task.Delay(Timeout.InfiniteTimeSpan);
}

static async Task RunGitShimAsync(string[] arguments)
{
    var mode = Environment.GetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE");

    if (arguments[0] == "clone")
    {
        var targetPath = arguments[^1];
        Directory.CreateDirectory(targetPath);
        var partialPath = Path.Combine(targetPath, ".gittool-partial");
        await File.WriteAllTextAsync(partialPath, "partial clone data");
        Console.WriteLine($"CLONE_READY={targetPath}");
        Console.Out.Flush();

        if (mode == "complete")
        {
            File.Delete(partialPath);
            await File.WriteAllTextAsync(Path.Combine(targetPath, "README.md"), "completed clone");
            return;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan);
        return;
    }

    var operation = arguments.Length > 2 ? arguments[2] : "unknown";
    Console.WriteLine($"REPOSITORY_OPERATION_READY={operation}");
    Console.Out.Flush();

    if (mode != "complete")
    {
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}

static (TaskCompletionSource<bool> Signal, IProgress<string> Progress) CreateProgressSignal(
    string expectedText)
{
    var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var progress = new CallbackProgress<string>(line =>
    {
        if (line.StartsWith(expectedText, StringComparison.Ordinal))
        {
            signal.TrySetResult(true);
        }
    });
    return (signal, progress);
}

static string GetTestExecutablePath()
{
    var executable = Path.ChangeExtension(typeof(TestLogger).Assembly.Location, ".exe");
    return File.Exists(executable)
        ? executable
        : Environment.ProcessPath
          ?? throw new InvalidOperationException("The test executable path is unavailable.");
}

static bool HasProcessExited(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        return process.HasExited || process.WaitForExit(2_000);
    }
    catch (ArgumentException)
    {
        return true;
    }
}

static void TryDeleteTestDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
    catch
    {
        // Test cleanup is best-effort after all process handles have been drained.
    }
}

static async Task RunLocalRepositoryIntegrationTestsAsync()
{
    var testRoot = Path.Combine(Path.GetTempPath(), "GitTool.Tests", Guid.NewGuid().ToString("N"));
    var repositoryPath = Path.Combine(testRoot, "working");
    var remotePath = Path.Combine(testRoot, "remote.git");
    Directory.CreateDirectory(testRoot);

    try
    {
        var runner = new ProcessRunner();
        await RequireGitSuccessAsync(runner, ["init", "--bare", remotePath], testRoot);
        await RequireGitSuccessAsync(runner, ["init", "--initial-branch=main", repositoryPath], testRoot);
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "config", "user.name", "GitTool Tests"], testRoot);
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "config", "user.email", "tests@example.invalid"], testRoot);
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "config", "commit.gpgsign", "false"], testRoot);

        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "# GitTool integration test");
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "add", "README.md"], testRoot);
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "commit", "-m", "Initial test commit"], testRoot);
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "remote", "add", "origin", remotePath], testRoot);
        await RequireGitSuccessAsync(runner, ["-C", repositoryPath, "push", "-u", "origin", "main"], testRoot);

        var logger = new TestLogger();
        var client = new GitClient(runner, new GitUrlResolver(), new GitHubSshProbe(runner), logger);
        var inspection = await client.InspectRepositoryAsync(repositoryPath, CancellationToken.None);
        if (!inspection.IsGitRepository || inspection.Repository?.Branch != "main")
        {
            throw new InvalidOperationException("[FAIL] Repository inspection did not identify the local main branch.");
        }

        var executor = new GitCommandExecutor(runner, logger);
        var registry = new RepositoryOperationRegistry(
        [
            new FetchRepositoryOperation(executor),
            new PullRepositoryOperation(executor),
            new PushRepositoryOperation(executor)
        ]);
        var options = new RepositoryOperationOptions(false);

        foreach (var operation in new[] { "fetch", "pull", "push" })
        {
            var result = await registry.ExecuteAsync(
                operation,
                repositoryPath,
                options,
                null,
                CancellationToken.None);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"[FAIL] Local Git {operation} failed: {result.Diagnostics}");
            }
        }
    }
    finally
    {
        try
        {
            Directory.Delete(testRoot, true);
        }
        catch
        {
            // The OS can release Git file handles shortly after the process exits.
        }
    }
}

static async Task RequireGitSuccessAsync(
    ProcessRunner runner,
    IReadOnlyList<string> arguments,
    string workingDirectory)
{
    var result = await runner.RunAsync(
        "git",
        arguments,
        workingDirectory,
        null,
        CancellationToken.None,
        TimeSpan.FromSeconds(20));

    if (!result.IsSuccess)
    {
        throw new InvalidOperationException(
            $"[FAIL] Git test setup failed: {result.StandardError}{result.StartError}");
    }
}

file sealed class TestLogger : IAppLogger
{
    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public Task ErrorAsync(string message, Exception? exception = null) => Task.CompletedTask;

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file sealed class FixedSshProbe(bool result) : IGitHubSshProbe
{
    public Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(result);
}

file sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
