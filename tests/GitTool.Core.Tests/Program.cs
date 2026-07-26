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

    if (args[0] == "--echo-stdin")
    {
        Console.Write(await Console.In.ReadToEndAsync());
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

RunGitHistoryParserTests();
RunGitHistoryGraphTests();
await RunProcessStandardInputTestsAsync();
await RunProcessCancellationTestsAsync();
await RunOperationCoordinatorCancellationTestsAsync();
await RunCloneCancellationCleanupTestsAsync();
await RunRepositoryCancellationPreservationTestsAsync();
await RunGitHistoryCancellationTestsAsync();
await RunGitHistoryTimeoutTestsAsync();
await RunGitHistoryStartupFailureTestsAsync();
await RunLocalRepositoryIntegrationTestsAsync();
Console.WriteLine(
    $"[OK] {tests.Length} URL cases, history visualization checks, cancellation safety checks, clone cleanup checks, and local Git integration checks passed.");
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

static async Task RunProcessStandardInputTestsAsync()
{
    const string input = "first line\nsecond line\n";
    var executable = GetTestExecutablePath();
    var result = await new ProcessRunner().RunAsync(
        executable,
        ["--echo-stdin"],
        Path.GetDirectoryName(executable),
        null,
        CancellationToken.None,
        standardInput: input);

    AssertTrue(result.IsSuccess, "Process standard input could not be written.");
    AssertEqual(
        input.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
        result.StandardOutput,
        "captured standard input echo");
}

static void RunGitHistoryParserTests()
{
    const char field = '\u001f';
    const char record = '\u001e';
    var historyOutput = string.Join(
        string.Empty,
        $"merge-hash{field}merge{field}main-hash branch-hash{field}Test Author{field}author@example.invalid{field}2026-07-26T10:15:00+05:30{field}HEAD -> main, tag: v0.1{field}Merge experiment{record}",
        $"\nmain-hash{field}main{field}root-hash{field}Test Author{field}author@example.invalid{field}2026-07-25T10:15:00+05:30{field}{field}Main work{record}");

    var commits = GitHistoryParser.ParseHistory(historyOutput);
    AssertEqual(2, commits.Count, "parsed history commit count");
    AssertEqual("merge-hash", commits[0].Hash, "parsed merge hash");
    AssertEqual(2, commits[0].ParentHashes.Count, "parsed merge parents");
    AssertEqual(2, commits[0].References.Count, "parsed references");
    AssertTrue(commits[0].IsHead, "HEAD decoration was not recognized.");
    AssertEqual(
        CommitSignatureStatus.Unknown,
        commits[0].SignatureStatus,
        "history signature status before enrichment");

    var detailsOutput =
        $"merge-hash{field}merge{field}main-hash branch-hash{field}Test Author{field}author@example.invalid{field}2026-07-26T10:15:00+05:30{field}HEAD -> main{field}Merge experiment{field}Merge experiment\n\nFull commit body.{record}\n"
        + "12\t3\tsrc/Feature.cs\n"
        + "0\t0\told-name.txt => new-name.txt\n"
        + "-\t-\tAssets/image.png\n";
    var details = GitHistoryParser.ParseDetails(detailsOutput);

    AssertEqual("Merge experiment\n\nFull commit body.", details.Message, "parsed full commit message");
    AssertEqual(3, details.Files.Count, "parsed changed-file count");
    AssertEqual("old-name.txt => new-name.txt", details.Files[1].Path, "parsed rename path");
    AssertEqual<int?>(null, details.Files[2].Additions, "parsed binary additions");
    AssertEqual(12, details.TotalAdditions, "total additions");
    AssertEqual(3, details.TotalDeletions, "total deletions");
    AssertEqual(
        CommitSignatureStatus.Unknown,
        details.Commit.SignatureStatus,
        "detail signature status before enrichment");

    var signatureBatchOutput =
        """
        merge-hash commit 140
        tree tree-hash
        parent main-hash
        gpgsig -----BEGIN PGP SIGNATURE-----
         signature-data
         -----END PGP SIGNATURE-----

        Merge message

        main-hash commit 80
        tree root-tree-hash
        parent root-hash

        Main message

        """;
    var signatureStatuses = GitHistoryParser.ParseSignaturePresenceBatch(
        signatureBatchOutput,
        ["merge-hash", "main-hash"]);
    AssertEqual(
        CommitSignatureStatus.Signed,
        signatureStatuses["merge-hash"],
        "raw signed commit presence");
    AssertEqual(
        CommitSignatureStatus.Unsigned,
        signatureStatuses["main-hash"],
        "raw unsigned commit presence");

    var sha256SignatureStatus = GitHistoryParser.ParseSignaturePresenceBatch(
        """
        sha256-hash commit 90
        tree tree-hash
        gpgsig-sha256 -----BEGIN SSH SIGNATURE-----
         signature-data

        Message

        """,
        ["sha256-hash"]);
    AssertEqual(
        CommitSignatureStatus.Signed,
        sha256SignatureStatus["sha256-hash"],
        "raw SHA-256 signature presence");

    var malformedHistoryRejected = false;
    try
    {
        GitHistoryParser.ParseHistory("incomplete");
    }
    catch (FormatException)
    {
        malformedHistoryRejected = true;
    }

    AssertTrue(malformedHistoryRejected, "Malformed history output was not rejected.");

    var malformedSignatureBatchRejected = false;
    try
    {
        GitHistoryParser.ParseSignaturePresenceBatch(
            "different-hash commit 20\ntree tree-hash\n\nMessage\n",
            ["expected-hash"]);
    }
    catch (FormatException)
    {
        malformedSignatureBatchRejected = true;
    }

    AssertTrue(
        malformedSignatureBatchRejected,
        "Malformed signature-presence output was not rejected.");
}

static void RunGitHistoryGraphTests()
{
    var timestamp = DateTimeOffset.Parse("2026-07-26T10:15:00+05:30");
    GitCommitInfo Commit(string hash, params string[] parents) =>
        new(hash, hash, parents, "Test Author", "author@example.invalid", timestamp, [], hash);

    var rows = GitHistoryGraphBuilder.Build(
    [
        Commit("merge", "main", "branch"),
        Commit("main", "root"),
        Commit("branch", "root"),
        Commit("root")
    ]);

    AssertEqual(4, rows.Count, "history graph row count");
    AssertEqual(0, rows[0].NodeLane, "merge node lane");
    AssertEqual(2, rows[0].ParentLanes.Count, "merge parent lane count");
    AssertEqual(0, rows[0].ParentLanes[0], "first-parent lane");
    AssertEqual(1, rows[0].ParentLanes[1], "second-parent lane");
    AssertTrue(rows[1].ContinuingLanes.Contains(1), "Branch lane did not continue beside main.");
    AssertEqual(1, rows[2].NodeLane, "branch node lane");
    AssertEqual(0, rows[2].ParentLanes[0], "branch convergence lane");
    AssertTrue(rows[3].HasIncomingEdge, "Root commit did not receive the converged edge.");
    AssertTrue(
        rows.All(row => row.GraphWidth.Equals(rows[0].GraphWidth)),
        "History rows did not receive a stable graph width.");
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

        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "slow-readonly");
        using (var cancellation = new CancellationTokenSource())
        {
            var cloneReady = CreateProgressSignal("CLONE_READY=");
            var cloneTask = client.CloneAsync(request, cloneReady.Progress, cancellation.Token);
            await cloneReady.Signal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
            var result = await cloneTask.WaitAsync(TimeSpan.FromSeconds(5));

            AssertTrue(result.IsCancelled, "A clone with a read-only pack file was not cancelled.");
            AssertTrue(
                result.Cancellation is { CleanupAttempted: true, CleanupSucceeded: true },
                "A read-only Git pack artifact prevented cancelled-clone cleanup.");
            AssertTrue(
                !Directory.Exists(targetPath),
                "The clone target containing a read-only Git pack artifact was not removed.");
        }

        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "complete");
        var retry = await client.CloneAsync(request, null, CancellationToken.None);
        AssertTrue(retry.IsSuccess, "The clone destination could not be reused after cancellation cleanup.");
        AssertTrue(Directory.Exists(targetPath), "The retry clone did not create its target directory.");
        AssertEqual(targetPath, retry.AffectedPath, "successful clone affected path");

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

static async Task RunGitHistoryCancellationTestsAsync()
{
    var testRoot = Path.Combine(
        Path.GetTempPath(),
        "GitTool.HistoryCancellation",
        Guid.NewGuid().ToString("N"));
    var originalMode = Environment.GetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE");
    Directory.CreateDirectory(testRoot);

    try
    {
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "slow");
        var client = new GitClient(
            new ProcessRunner(),
            new GitUrlResolver(),
            new FixedSshProbe(false),
            new TestLogger(),
            GetTestExecutablePath());

        using var historyCancellation = new CancellationTokenSource();
        var historyTask = client.GetHistoryAsync(testRoot, historyCancellation.Token);
        await Task.Delay(150);
        await historyCancellation.CancelAsync();
        var history = await historyTask.WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(history.IsCancelled, "History loading cancellation was not reported.");

        using var detailsCancellation = new CancellationTokenSource();
        var detailsTask = client.GetCommitDetailsAsync(
            testRoot,
            "test-hash",
            detailsCancellation.Token);
        await Task.Delay(150);
        await detailsCancellation.CancelAsync();
        var details = await detailsTask.WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(details.IsCancelled, "Commit-detail loading cancellation was not reported.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", originalMode);
        TryDeleteTestDirectory(testRoot);
    }
}

static async Task RunGitHistoryStartupFailureTestsAsync()
{
    var testRoot = Path.Combine(
        Path.GetTempPath(),
        "GitTool.HistoryStartupFailure",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testRoot);

    try
    {
        var client = new GitClient(
            new ProcessRunner(),
            new GitUrlResolver(),
            new FixedSshProbe(false),
            new TestLogger(),
            Path.Combine(testRoot, "missing-git.exe"));
        var history = await client.GetHistoryAsync(testRoot, CancellationToken.None);
        AssertTrue(
            !history.IsSuccess
            && history.ErrorMessage.Contains("could not be started", StringComparison.OrdinalIgnoreCase),
            "History loading did not report a Git startup failure.");

        var details = await client.GetCommitDetailsAsync(
            testRoot,
            "test-hash",
            CancellationToken.None);
        AssertTrue(
            !details.IsSuccess
            && details.ErrorMessage.Contains("could not be started", StringComparison.OrdinalIgnoreCase),
            "Commit-detail loading did not report a Git startup failure.");
    }
    finally
    {
        TryDeleteTestDirectory(testRoot);
    }
}

static async Task RunGitHistoryTimeoutTestsAsync()
{
    var testRoot = Path.Combine(
        Path.GetTempPath(),
        "GitTool.HistoryTimeout",
        Guid.NewGuid().ToString("N"));
    var originalMode = Environment.GetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE");
    Directory.CreateDirectory(testRoot);

    try
    {
        Environment.SetEnvironmentVariable("GITTOOL_TEST_SHIM_MODE", "slow");
        var client = new GitClient(
            new ProcessRunner(),
            new GitUrlResolver(),
            new FixedSshProbe(false),
            new TestLogger(),
            GetTestExecutablePath());
        var history = await client.GetHistoryAsync(
                testRoot,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(20));

        AssertTrue(!history.IsCancelled, "A history timeout was reported as cancellation.");
        AssertTrue(
            !history.IsSuccess
            && history.ErrorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase),
            "History loading did not report its process timeout.");
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

        if (mode == "slow-readonly")
        {
            var packDirectory = Path.Combine(targetPath, ".git", "objects", "pack");
            Directory.CreateDirectory(packDirectory);
            var packPath = Path.Combine(packDirectory, "tmp_pack_GitToolTest");
            await File.WriteAllTextAsync(packPath, "partial pack data");
            File.SetAttributes(packPath, File.GetAttributes(packPath) | FileAttributes.ReadOnly);
        }

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

        await RequireGitSuccessAsync(
            runner,
            ["-C", repositoryPath, "switch", "-c", "feature/history-test"],
            testRoot);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "Feature.txt"),
            "feature branch content");
        await RequireGitSuccessAsync(
            runner,
            ["-C", repositoryPath, "add", "Feature.txt"],
            testRoot);
        await RequireGitSuccessAsync(
            runner,
            ["-C", repositoryPath, "commit", "-m", "Add feature branch commit"],
            testRoot);

        await RequireGitSuccessAsync(
            runner,
            ["-C", repositoryPath, "switch", "main"],
            testRoot);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "Main.txt"),
            "main branch content");
        await RequireGitSuccessAsync(
            runner,
            ["-C", repositoryPath, "add", "Main.txt"],
            testRoot);
        await RequireGitSuccessAsync(
            runner,
            ["-C", repositoryPath, "commit", "-m", "Add main branch commit"],
            testRoot);
        await RequireGitSuccessAsync(
            runner,
            [
                "-C", repositoryPath,
                "merge",
                "--no-ff",
                "feature/history-test",
                "-m",
                "Merge feature branch"
            ],
            testRoot);

        var logger = new TestLogger();
        var client = new GitClient(runner, new GitUrlResolver(), new GitHubSshProbe(runner), logger);
        var inspection = await client.InspectRepositoryAsync(repositoryPath, CancellationToken.None);
        if (!inspection.IsGitRepository || inspection.Repository?.Branch != "main")
        {
            throw new InvalidOperationException("[FAIL] Repository inspection did not identify the local main branch.");
        }

        var history = await client.GetHistoryAsync(repositoryPath, CancellationToken.None);
        AssertTrue(history.IsSuccess, $"Local history loading failed: {history.Diagnostics}");
        AssertEqual(4, history.Commits.Count, "local history commit count");
        AssertEqual("Merge feature branch", history.Commits[0].Subject, "newest local history subject");
        AssertEqual(2, history.Commits[0].ParentHashes.Count, "local merge parent count");
        AssertTrue(
            history.Commits.All(commit => !commit.IsSigned),
            "Unsigned local commits were reported as signed.");
        AssertTrue(
            history.Commits.Select(commit => commit.Subject).ToHashSet().SetEquals(
            [
                "Initial test commit",
                "Add feature branch commit",
                "Add main branch commit",
                "Merge feature branch"
            ]),
            "Local history did not contain the complete diverged and merged topology.");

        var graphRows = GitHistoryGraphBuilder.Build(history.Commits);
        AssertTrue(
            graphRows.Any(row => row.NodeLane > 0),
            "The real merge history did not produce a secondary graph lane.");

        var details = await client.GetCommitDetailsAsync(
            repositoryPath,
            history.Commits[0].Hash,
            CancellationToken.None);
        AssertTrue(details.IsSuccess, $"Local commit-detail loading failed: {details.Diagnostics}");
        AssertTrue(
            details.Details?.Files.Any(file => file.Path == "Feature.txt") == true,
            "Local merge details did not include Feature.txt.");
        AssertTrue(
            details.Details?.Commit.IsSigned == false,
            "Unsigned local commit details were reported as signed.");

        var invalidDetails = await client.GetCommitDetailsAsync(
            repositoryPath,
            "not-a-real-commit",
            CancellationToken.None);
        AssertTrue(!invalidDetails.IsSuccess, "An invalid commit was reported as successful.");

        var emptyRepositoryPath = Path.Combine(testRoot, "empty");
        await RequireGitSuccessAsync(
            runner,
            ["init", "--initial-branch=main", emptyRepositoryPath],
            testRoot);
        var emptyHistory = await client.GetHistoryAsync(
            emptyRepositoryPath,
            CancellationToken.None);
        AssertTrue(emptyHistory.IsSuccess, $"Empty repository history failed: {emptyHistory.Diagnostics}");
        AssertEqual(0, emptyHistory.Commits.Count, "empty repository history count");

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
