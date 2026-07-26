using System.Diagnostics;
using GitTool.Core.Infrastructure;

namespace GitTool.App.Services;

internal enum RepositoryLaunchTarget
{
    Terminal,
    VisualStudioCode,
    VisualStudio,
    FileExplorer
}

internal sealed record RepositoryLauncherAvailability(
    string? TerminalExecutable,
    string? VisualStudioCodeExecutable,
    string? VisualStudioExecutable,
    IReadOnlyList<string> VisualStudioSolutions)
{
    public bool CanOpenTerminal => !string.IsNullOrWhiteSpace(TerminalExecutable);

    public bool CanOpenVisualStudioCode =>
        !string.IsNullOrWhiteSpace(VisualStudioCodeExecutable);

    public bool CanOpenVisualStudio =>
        !string.IsNullOrWhiteSpace(VisualStudioExecutable)
        && VisualStudioSolutions.Count > 0;
}

internal interface IRepositoryLauncherPlatform
{
    Task<RepositoryLauncherAvailability> DiscoverAsync(
        string repositoryPath,
        CancellationToken cancellationToken);

    void Start(ProcessStartInfo startInfo);
}

internal sealed class RepositoryLauncherService
{
    private readonly IRepositoryLauncherPlatform _platform;

    public RepositoryLauncherService(ProcessRunner processRunner)
        : this(new WindowsRepositoryLauncherPlatform(processRunner))
    {
    }

    internal RepositoryLauncherService(IRepositoryLauncherPlatform platform)
    {
        _platform = platform;
    }

    public Task<RepositoryLauncherAvailability> DiscoverAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default) =>
        _platform.DiscoverAsync(repositoryPath, cancellationToken);

    public void Launch(
        RepositoryLaunchTarget target,
        string repositoryPath,
        RepositoryLauncherAvailability availability,
        string? solutionPath = null)
    {
        var normalizedRepositoryPath = Path.GetFullPath(repositoryPath);
        var startInfo = target switch
        {
            RepositoryLaunchTarget.Terminal => CreateStartInfo(
                RequireExecutable(
                    availability.TerminalExecutable,
                    "Windows Terminal"),
                normalizedRepositoryPath,
                "-d",
                normalizedRepositoryPath),
            RepositoryLaunchTarget.VisualStudioCode => CreateStartInfo(
                RequireExecutable(
                    availability.VisualStudioCodeExecutable,
                    "Visual Studio Code"),
                normalizedRepositoryPath,
                "--new-window",
                normalizedRepositoryPath),
            RepositoryLaunchTarget.VisualStudio => CreateVisualStudioStartInfo(
                normalizedRepositoryPath,
                availability,
                solutionPath),
            RepositoryLaunchTarget.FileExplorer => CreateStartInfo(
                "explorer.exe",
                normalizedRepositoryPath,
                normalizedRepositoryPath),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
        };

        _platform.Start(startInfo);
    }

    private static ProcessStartInfo CreateVisualStudioStartInfo(
        string repositoryPath,
        RepositoryLauncherAvailability availability,
        string? solutionPath)
    {
        var executable = RequireExecutable(
            availability.VisualStudioExecutable,
            "Visual Studio");
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            throw new InvalidOperationException("Select a Visual Studio solution to open.");
        }

        var normalizedSolutionPath = Path.GetFullPath(solutionPath);
        var repositoryPrefix = Path.EndsInDirectorySeparator(repositoryPath)
            ? repositoryPath
            : repositoryPath + Path.DirectorySeparatorChar;
        if (!normalizedSolutionPath.StartsWith(
                repositoryPrefix,
                StringComparison.OrdinalIgnoreCase)
            || !availability.VisualStudioSolutions.Any(candidate =>
                string.Equals(
                    Path.GetFullPath(candidate),
                    normalizedSolutionPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The selected solution is outside the managed repository.");
        }

        return CreateStartInfo(
            executable,
            repositoryPath,
            normalizedSolutionPath);
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string RequireExecutable(string? executable, string applicationName) =>
        !string.IsNullOrWhiteSpace(executable)
            ? executable
            : throw new InvalidOperationException(
                $"{applicationName} is not available on this computer.");
}

internal sealed class WindowsRepositoryLauncherPlatform(ProcessRunner processRunner)
    : IRepositoryLauncherPlatform
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(3);

    public async Task<RepositoryLauncherAvailability> DiscoverAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var normalizedRepositoryPath = Path.GetFullPath(repositoryPath);
        var terminal = FindExecutable(
            "wt.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps",
                "wt.exe"));
        var visualStudioCode = FindExecutable(
            "code.exe",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Microsoft VS Code",
                "Code.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft VS Code",
                "Code.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft VS Code",
                "Code.exe"))
            ?? FindExecutable("code.cmd");

        var visualStudioTask = FindVisualStudioAsync(
            normalizedRepositoryPath,
            cancellationToken);
        var solutionsTask = FindVisualStudioSolutionsAsync(
            normalizedRepositoryPath,
            cancellationToken);
        await Task.WhenAll(visualStudioTask, solutionsTask);

        return new RepositoryLauncherAvailability(
            terminal,
            visualStudioCode,
            await visualStudioTask,
            await solutionsTask);
    }

    public void Start(ProcessStartInfo startInfo)
    {
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Windows could not start '{startInfo.FileName}'.");
    }

    private async Task<string?> FindVisualStudioAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        var result = await processRunner.RunAsync(
                vswhere,
                ["-latest", "-products", "*", "-property", "productPath"],
                workingDirectory,
                null,
                cancellationToken,
                DiscoveryTimeout)
            .ConfigureAwait(false);
        var productPath = result.IsSuccess
            ? result.StandardOutput.Trim()
            : string.Empty;
        return File.Exists(productPath) ? productPath : null;
    }

    private async Task<IReadOnlyList<string>> FindVisualStudioSolutionsAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var solutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var extension in new[] { "*.slnx", "*.sln" })
            {
                foreach (var path in Directory.EnumerateFiles(
                             repositoryPath,
                             extension,
                             SearchOption.TopDirectoryOnly))
                {
                    solutions.Add(Path.GetFullPath(path));
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // Git's tracked-file query below can still find accessible solutions.
        }

        var result = await processRunner.RunAsync(
                "git",
                [
                    "-C", repositoryPath,
                    "ls-files",
                    "--",
                    "*.sln",
                    "*.slnx"
                ],
                repositoryPath,
                null,
                cancellationToken,
                DiscoveryTimeout)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            foreach (var relativePath in result.StandardOutput.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fullPath = Path.GetFullPath(
                    Path.Combine(repositoryPath, relativePath));
                if (File.Exists(fullPath))
                {
                    solutions.Add(fullPath);
                }
            }
        }

        return solutions
            .OrderBy(path => Path.GetExtension(path).Equals(
                ".slnx",
                StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(path => Path.GetRelativePath(repositoryPath, path))
            .ToArray();
    }

    private static string? FindExecutable(
        string executableName,
        params string[] additionalCandidates)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathCandidates = pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), executableName));
        foreach (var candidate in pathCandidates.Concat(additionalCandidates))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                // Ignore malformed PATH entries and continue discovery.
            }
        }

        return null;
    }
}
