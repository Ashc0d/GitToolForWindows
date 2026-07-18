using GitTool.Core.Git;
using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

namespace GitTool.App.Services;

public sealed class AppServices
{
    public AppServices()
    {
        Paths = new AppPaths();
        Logger = new BufferedFileLogger(Paths);
        SettingsStore = new JsonSettingsStore(Paths);

        var processRunner = new ProcessRunner();
        var urlResolver = new GitUrlResolver();
        var sshProbe = new GitHubSshProbe(processRunner);
        GitClient = new GitClient(processRunner, urlResolver, sshProbe, Logger);

        var commandExecutor = new GitCommandExecutor(processRunner, Logger);
        RepositoryOperations = new RepositoryOperationRegistry(
        [
            new FetchRepositoryOperation(commandExecutor),
            new PullRepositoryOperation(commandExecutor),
            new PushRepositoryOperation(commandExecutor)
        ]);

        OperationCoordinator = new OperationCoordinator();
        BadgeService = new TaskbarBadgeService();
        NotificationService = new AppNotificationService(() => Settings);
        UserOperations = new UserOperationService(
            OperationCoordinator,
            BadgeService,
            NotificationService,
            Logger);
        FolderPicker = new FolderPickerService();
    }

    public AppPaths Paths { get; }

    public BufferedFileLogger Logger { get; }

    public JsonSettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; } = AppSettings.CreateDefault();

    public GitClient GitClient { get; }

    public RepositoryOperationRegistry RepositoryOperations { get; }

    public OperationCoordinator OperationCoordinator { get; }

    public TaskbarBadgeService BadgeService { get; }

    public AppNotificationService NotificationService { get; }

    public UserOperationService UserOperations { get; }

    public FolderPickerService FolderPicker { get; }

    public async Task InitializeAsync()
    {
        Settings = await SettingsStore.LoadAsync();
        Logger.Info($"Settings loaded from '{Paths.SettingsFile}'.");
    }

    public async Task ShutdownAsync()
    {
        BadgeService.Clear();
        await Logger.DisposeAsync().ConfigureAwait(false);
    }
}
