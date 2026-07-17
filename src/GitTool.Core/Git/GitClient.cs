using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class GitClient
{
    private readonly ProcessRunner _processRunner;
    private readonly GitUrlResolver _urlResolver;
    private readonly GitHubSshProbe _sshProbe;
    private readonly IAppLogger _logger;

    public GitClient(
        ProcessRunner processRunner,
        GitUrlResolver urlResolver,
        GitHubSshProbe sshProbe,
        IAppLogger logger)
    {
        _processRunner = processRunner;
        _urlResolver = urlResolver;
        _sshProbe = sshProbe;
        _logger = logger;
    }

    public async Task<OperationResult> CloneAsync(
        GitCloneRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RepositoryUrl))
        {
            return OperationResult.Failure("Enter a public repository URL to clone.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationRoot))
        {
            return OperationResult.Failure("Choose a destination folder for the repository.");
        }

        try
        {
            Directory.CreateDirectory(request.DestinationRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure(
                "The destination folder could not be created or accessed.",
                exception.Message);
        }

        progress?.Report("Checking GitHub SSH authentication…");
        var canUseSsh = await _sshProbe.CanAuthenticateAsync(cancellationToken).ConfigureAwait(false);

        ResolvedRepositoryUrl repository;
        try
        {
            repository = _urlResolver.Resolve(request.RepositoryUrl, canUseSsh);
        }
        catch (ArgumentException exception)
        {
            return OperationResult.Failure("The repository URL is not valid.", exception.Message);
        }

        var targetPath = GetSafeTargetPath(request.DestinationRoot, repository.RepositoryName);
        if (targetPath is null)
        {
            return OperationResult.Failure("The repository name produces an unsafe destination path.");
        }

        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            return OperationResult.Failure(
                $"The destination '{targetPath}' already exists and is not empty.");
        }

        var transportName = repository.Transport switch
        {
            GitTransport.Ssh => "SSH",
            GitTransport.Https => "HTTPS",
            _ => "the supplied URL"
        };
        progress?.Report($"Using {transportName}: cloning {repository.RepositoryName}…");

        var arguments = new List<string> { "clone", "--progress" };
        if (request.InitializeSubmodules)
        {
            arguments.Add("--recurse-submodules");
        }

        arguments.Add(repository.CloneUrl);
        arguments.Add(targetPath);
        _logger.Info($"Cloning '{repository.RepositoryName}' using {transportName} into '{targetPath}'.");

        var result = await _processRunner.RunAsync(
                "git",
                arguments,
                request.DestinationRoot,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Started)
        {
            return OperationResult.Failure(
                "Git could not be started. Install Git for Windows and ensure it is available on PATH.",
                result.StartError);
        }

        if (!result.IsSuccess)
        {
            return OperationResult.Failure(
                $"Clone failed while using {transportName}.",
                result.StandardError,
                result.StandardOutput,
                result.ExitCode);
        }

        return OperationResult.Success(
            $"Cloned {repository.RepositoryName} using {transportName}.",
            result.StandardOutput + result.StandardError);
    }

    public async Task<RepositoryInspectionResult> InspectRepositoryAsync(
        string selectedPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || !Directory.Exists(selectedPath))
        {
            return new RepositoryInspectionResult(
                false,
                null,
                "Choose an existing folder.");
        }

        var rootResult = await RunReadOnlyGitAsync(
                selectedPath,
                ["-C", selectedPath, "rev-parse", "--show-toplevel"],
                cancellationToken)
            .ConfigureAwait(false);

        if (!rootResult.IsSuccess)
        {
            return new RepositoryInspectionResult(
                false,
                null,
                "The selected folder is not a Git repository.",
                rootResult.StandardError + rootResult.StartError);
        }

        var rootPath = rootResult.StandardOutput.Trim();
        var branchResult = await RunReadOnlyGitAsync(
                rootPath,
                ["-C", rootPath, "branch", "--show-current"],
                cancellationToken)
            .ConfigureAwait(false);
        var remoteResult = await RunReadOnlyGitAsync(
                rootPath,
                ["-C", rootPath, "remote", "get-url", "origin"],
                cancellationToken)
            .ConfigureAwait(false);
        var statusResult = await RunReadOnlyGitAsync(
                rootPath,
                ["-C", rootPath, "status", "--porcelain"],
                cancellationToken)
            .ConfigureAwait(false);

        if (!statusResult.IsSuccess)
        {
            return new RepositoryInspectionResult(
                false,
                null,
                "Git could not read the repository status.",
                statusResult.StandardError);
        }

        var branch = branchResult.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(branch))
        {
            var commitResult = await RunReadOnlyGitAsync(
                    rootPath,
                    ["-C", rootPath, "rev-parse", "--short", "HEAD"],
                    cancellationToken)
                .ConfigureAwait(false);
            branch = commitResult.IsSuccess
                ? $"Detached at {commitResult.StandardOutput.Trim()}"
                : "Detached HEAD";
        }

        var remote = remoteResult.IsSuccess
            ? remoteResult.StandardOutput.Trim()
            : "No origin remote";
        var changedFiles = statusResult.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Length;

        return new RepositoryInspectionResult(
            true,
            new GitRepositoryInfo(rootPath, branch, remote, changedFiles),
            string.Empty);
    }

    private async Task<ProcessRunResult> RunReadOnlyGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return await _processRunner.RunAsync(
                "git",
                arguments,
                workingDirectory,
                null,
                cancellationToken,
                TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
    }

    private static string? GetSafeTargetPath(string destinationRoot, string repositoryName)
    {
        var root = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(root, repositoryName));
        var rootPrefix = root + Path.DirectorySeparatorChar;

        return target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? target
            : null;
    }
}
