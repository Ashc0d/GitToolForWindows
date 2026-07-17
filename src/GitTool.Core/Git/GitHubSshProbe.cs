using GitTool.Core.Infrastructure;

namespace GitTool.Core.Git;

public sealed class GitHubSshProbe : IGitHubSshProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);
    private readonly ProcessRunner _processRunner;

    public GitHubSshProbe(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
                "ssh",
                ["-T", "-o", "BatchMode=yes", "-o", "ConnectTimeout=8", "git@github.com"],
                null,
                null,
                cancellationToken,
                ProbeTimeout)
            .ConfigureAwait(false);

        if (result.IsCancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (!result.Started || result.TimedOut)
        {
            return false;
        }

        var response = result.StandardOutput + Environment.NewLine + result.StandardError;
        return response.Contains("successfully authenticated", StringComparison.OrdinalIgnoreCase)
               && response.Contains("github", StringComparison.OrdinalIgnoreCase);
    }
}
