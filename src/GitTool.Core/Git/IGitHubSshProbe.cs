namespace GitTool.Core.Git;

public interface IGitHubSshProbe
{
    Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken);
}
