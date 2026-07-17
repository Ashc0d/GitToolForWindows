using System.Text.RegularExpressions;
using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed partial class GitUrlResolver
{
    public ResolvedRepositoryUrl Resolve(string repositoryInput, bool preferSsh)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryInput);
        var input = repositoryInput.Trim().TrimEnd('/');

        if (TryGetGitHubCoordinates(input, out var owner, out var repository))
        {
            var cloneUrl = preferSsh
                ? $"git@github.com:{owner}/{repository}.git"
                : $"https://github.com/{owner}/{repository}.git";

            return new ResolvedRepositoryUrl(
                cloneUrl,
                repository,
                preferSsh ? GitTransport.Ssh : GitTransport.Https);
        }

        var repositoryName = GetRepositoryName(input);
        var transport = input.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                ? GitTransport.Ssh
                : input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                  || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? GitTransport.Https
                    : GitTransport.Original;

        return new ResolvedRepositoryUrl(input, repositoryName, transport);
    }

    public bool TryGetGitHubCoordinates(
        string repositoryInput,
        out string owner,
        out string repository)
    {
        var match = GitHubUrlRegex().Match(repositoryInput.Trim());
        if (!match.Success)
        {
            match = GitHubShorthandRegex().Match(repositoryInput.Trim());
        }

        if (!match.Success)
        {
            owner = string.Empty;
            repository = string.Empty;
            return false;
        }

        owner = match.Groups["owner"].Value;
        repository = StripGitSuffix(match.Groups["repository"].Value);
        return owner.Length > 0 && repository.Length > 0;
    }

    private static string GetRepositoryName(string input)
    {
        var normalized = input.Replace('\\', '/');
        var separatorIndex = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf(':'));
        var name = separatorIndex >= 0 ? normalized[(separatorIndex + 1)..] : normalized;
        name = StripGitSuffix(name);

        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The repository URL does not contain a valid repository name.");
        }

        return name;
    }

    private static string StripGitSuffix(string repository) =>
        repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repository[..^4]
            : repository;

    [GeneratedRegex(
        "^(?:(?:https?://github\\.com/)|(?:ssh://git@github\\.com/)|(?:git@github\\.com:))(?<owner>[A-Za-z0-9_.-]+)/(?<repository>[A-Za-z0-9_.-]+?)(?:\\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubUrlRegex();

    [GeneratedRegex(
        "^(?<owner>[A-Za-z0-9_.-]+)/(?<repository>[A-Za-z0-9_.-]+?)(?:\\.git)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex GitHubShorthandRegex();
}
