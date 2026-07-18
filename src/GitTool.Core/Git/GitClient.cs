using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class GitClient
{
    private readonly ProcessRunner _processRunner;
    private readonly GitUrlResolver _urlResolver;
    private readonly IGitHubSshProbe _sshProbe;
    private readonly IAppLogger _logger;
    private readonly string _gitExecutable;

    public GitClient(
        ProcessRunner processRunner,
        GitUrlResolver urlResolver,
        IGitHubSshProbe sshProbe,
        IAppLogger logger,
        string gitExecutable = "git")
    {
        _processRunner = processRunner;
        _urlResolver = urlResolver;
        _sshProbe = sshProbe;
        _logger = logger;
        _gitExecutable = gitExecutable;
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
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
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

        if (File.Exists(targetPath))
        {
            return OperationResult.Failure(
                $"The destination '{targetPath}' already exists as a file.");
        }

        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            return OperationResult.Failure(
                $"The destination '{targetPath}' already exists and is not empty.");
        }

        var targetWasAbsentBeforeClone = !Directory.Exists(targetPath);

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
                _gitExecutable,
                arguments,
                request.DestinationRoot,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsCancelled)
        {
            return await BuildCancelledCloneResultAsync(
                    targetPath,
                    targetWasAbsentBeforeClone,
                    result)
                .ConfigureAwait(false);
        }

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
                _gitExecutable,
                arguments,
                workingDirectory,
                null,
                cancellationToken,
                TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);
    }

    private static string? GetSafeTargetPath(string destinationRoot, string repositoryName)
    {
        var root = Path.GetFullPath(destinationRoot);
        var target = Path.GetFullPath(Path.Combine(root, repositoryName));
        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;

        return target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? target
            : null;
    }

    private async Task<OperationResult> BuildCancelledCloneResultAsync(
        string targetPath,
        bool targetWasAbsentBeforeClone,
        ProcessRunResult processResult)
    {
        if (!targetWasAbsentBeforeClone)
        {
            _logger.Info($"Clone was cancelled; preserving pre-existing destination '{targetPath}'.");
            return OperationResult.Cancelled(
                "Clone was cancelled. The pre-existing destination was preserved.",
                processResult.StandardError,
                processResult.StandardOutput,
                processResult.ExitCode);
        }

        if (!Directory.Exists(targetPath))
        {
            return OperationResult.Cancelled(
                "Clone was cancelled before an incomplete repository was created.",
                processResult.StandardError,
                processResult.StandardOutput,
                processResult.ExitCode);
        }

        Exception? cleanupException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                DeleteDirectoryTreeWithoutFollowingReparsePoints(targetPath);
                _logger.Info($"Removed incomplete clone destination '{targetPath}' after cancellation.");
                return OperationResult.Cancelled(
                    "Clone was cancelled and the incomplete repository was removed.",
                    processResult.StandardError,
                    processResult.StandardOutput,
                    processResult.ExitCode,
                    new OperationCancellationMetadata(true, true));
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                cleanupException = exception;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt)).ConfigureAwait(false);
                }
            }
        }

        var cleanupError =
            $"Cleanup could not remove '{targetPath}': {cleanupException?.Message ?? "Unknown cleanup error."}";
        var diagnostics = string.IsNullOrWhiteSpace(processResult.StandardError)
            ? cleanupError
            : processResult.StandardError.TrimEnd() + Environment.NewLine + cleanupError;

        _logger.Warning(cleanupError);
        return OperationResult.Cancelled(
            $"Clone was cancelled, but the incomplete repository remains at '{targetPath}'.",
            diagnostics,
            processResult.StandardOutput,
            processResult.ExitCode,
            new OperationCancellationMetadata(true, false, targetPath));
    }

    private static void DeleteDirectoryTreeWithoutFollowingReparsePoints(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        var directory = new DirectoryInfo(directoryPath);
        ClearReadOnlyAttribute(directory);

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
            var isReparsePoint = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

            if (isDirectory && !isReparsePoint)
            {
                DeleteDirectoryTreeWithoutFollowingReparsePoints(entry.FullName);
                continue;
            }

            ClearReadOnlyAttribute(entry);
            if (isDirectory)
            {
                Directory.Delete(entry.FullName, false);
            }
            else
            {
                File.Delete(entry.FullName);
            }
        }

        Directory.Delete(directoryPath, false);
    }

    private static void ClearReadOnlyAttribute(FileSystemInfo entry)
    {
        if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            entry.Attributes &= ~FileAttributes.ReadOnly;
        }
    }
}
