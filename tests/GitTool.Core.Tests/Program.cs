using GitTool.Core.Git;
using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

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

await RunLocalRepositoryIntegrationTestsAsync();
Console.WriteLine($"[OK] {tests.Length} URL cases and local fetch/pull/push integration checks passed.");
return;

static void AssertEqual<T>(T expected, T actual, string subject)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"[FAIL] Expected {subject} to be '{expected}', but got '{actual}'.");
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
