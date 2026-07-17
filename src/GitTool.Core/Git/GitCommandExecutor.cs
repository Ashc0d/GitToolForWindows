using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

namespace GitTool.Core.Git;

public sealed class GitCommandExecutor : IGitCommandExecutor
{
    private readonly ProcessRunner _processRunner;
    private readonly IAppLogger _logger;

    public GitCommandExecutor(ProcessRunner processRunner, IAppLogger logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<OperationResult> RunRepositoryCommandAsync(
        string repositoryPath,
        string operationName,
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var commandArguments = new List<string> { "-C", repositoryPath };
        commandArguments.AddRange(arguments);
        _logger.Info($"Starting Git {operationName} in '{repositoryPath}'.");

        var result = await _processRunner.RunAsync(
                "git",
                commandArguments,
                repositoryPath,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Started)
        {
            return OperationResult.Failure(
                "Git could not be started. Make sure Git for Windows is installed and available on PATH.",
                result.StartError);
        }

        if (!result.IsSuccess)
        {
            return OperationResult.Failure(
                $"Git {operationName} failed.",
                result.StandardError,
                result.StandardOutput,
                result.ExitCode);
        }

        return OperationResult.Success(
            $"Git {operationName} completed.",
            result.StandardOutput + result.StandardError);
    }
}
