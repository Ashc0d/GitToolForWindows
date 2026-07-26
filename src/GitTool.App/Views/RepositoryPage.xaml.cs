using System.Text;
using GitTool.App.Services;
using GitTool.Core.Git;
using GitTool.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace GitTool.App.Views;

public sealed partial class RepositoryPage : Page
{
    private const double SideBySideHistoryMinimumWidth = 850;

    private GitRepositoryInfo? _repository;
    private CancellationTokenSource? _historyCancellation;
    private CancellationTokenSource? _commitDetailsCancellation;
    private CancellationTokenSource? _launcherCancellation;
    private GitCommitDetails? _selectedCommitDetails;
    private RepositoryLauncherAvailability? _launcherAvailability;
    private string? _loadedHistoryRoot;
    private bool _historyLoaded;
    private bool _mainWindowLayoutSubscribed;
    private bool _recentRepositoriesSubscribed;
    private long _historyRequestVersion;
    private long _commitDetailsRequestVersion;
    private long _launcherRequestVersion;
    private bool? _isHistorySideBySide;

    public RepositoryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        RepositorySelectorBar.SelectedItem = OverviewSelectorItem;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        HistoryListView.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnNestedScrollPointerWheelChanged),
            true);
        CommitDetailsScrollViewer.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnNestedScrollPointerWheelChanged),
            true);
        RecentRepositoriesList.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnNestedScrollPointerWheelChanged),
            true);
    }

    private void OnHistoryContentSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateHistoryLayout(e.NewSize.Width);
        DispatcherQueue.TryEnqueue(
            () => UpdateHistoryLayout(HistoryContentGrid.ActualWidth));
    }

    private void UpdateHistoryLayout(double availableWidth)
    {
        var useSideBySideLayout =
            App.Current.IsMainWindowMaximized
            || availableWidth >= SideBySideHistoryMinimumWidth;

        if (_isHistorySideBySide == useSideBySideLayout)
        {
            return;
        }

        _isHistorySideBySide = useSideBySideLayout;
        Grid.SetColumnSpan(HistoryListCard, useSideBySideLayout ? 1 : 2);
        Grid.SetColumn(CommitDetailsCard, useSideBySideLayout ? 1 : 0);
        Grid.SetRow(CommitDetailsCard, useSideBySideLayout ? 0 : 1);
        Grid.SetColumnSpan(CommitDetailsCard, useSideBySideLayout ? 1 : 2);
        HistoryListView.Height = useSideBySideLayout ? 600 : 520;
        CommitDetailsHost.Height = useSideBySideLayout ? 560 : 460;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_mainWindowLayoutSubscribed)
        {
            App.Current.MainWindowLayoutChanged += OnMainWindowLayoutChanged;
            _mainWindowLayoutSubscribed = true;
        }

        if (!_recentRepositoriesSubscribed)
        {
            App.Current.Services.RecentRepositories.RepositoriesChanged +=
                OnRecentRepositoriesChanged;
            _recentRepositoriesSubscribed = true;
        }

        RefreshRecentRepositories();

        if (RepositorySelectorBar.SelectedItem == HistorySelectorItem
            && _repository is not null
            && !_historyLoaded)
        {
            await LoadHistoryAsync();
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_mainWindowLayoutSubscribed)
        {
            App.Current.MainWindowLayoutChanged -= OnMainWindowLayoutChanged;
            _mainWindowLayoutSubscribed = false;
        }

        if (_recentRepositoriesSubscribed)
        {
            App.Current.Services.RecentRepositories.RepositoriesChanged -=
                OnRecentRepositoriesChanged;
            _recentRepositoriesSubscribed = false;
        }

        CancelPendingHistoryRequests();
        CancelLauncherDiscovery();
    }

    private void OnMainWindowLayoutChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(
            () => UpdateHistoryLayout(HistoryContentGrid.ActualWidth));
    }

    private async void OnChooseRepositoryClick(object sender, RoutedEventArgs e)
    {
        var selectedPath = await App.Current.Services.FolderPicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        RepositoryPathTextBox.Text = selectedPath;
        await RefreshRepositoryAsync(selectedPath);
    }

    private async void OnRefreshRepositoryClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(RepositoryPathTextBox.Text))
        {
            await RefreshRepositoryAsync(
                RepositoryPathTextBox.Text,
                recordRecentRepository: false);
        }
    }

    private async void OnOpenRecentRepositoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string repositoryPath })
        {
            RecentRepositoriesExpander.IsExpanded = false;
            RepositoryPathTextBox.Text = repositoryPath;
            await RefreshRepositoryAsync(repositoryPath);
        }
    }

    private async void OnRemoveRecentRepositoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string repositoryPath })
        {
            return;
        }

        try
        {
            await App.Current.Services.RecentRepositories.RemoveAsync(repositoryPath);
        }
        catch (Exception exception)
        {
            await App.Current.Services.Logger.ErrorAsync(
                $"Could not remove recent repository '{repositoryPath}'.",
                exception);
            ShowRepositoryMessage(
                InfoBarSeverity.Error,
                "Recent repository was not removed",
                exception.Message);
        }
    }

    private void OnOpenTerminalClick(object sender, RoutedEventArgs e) =>
        LaunchRepository(RepositoryLaunchTarget.Terminal);

    private void OnOpenVisualStudioCodeClick(object sender, RoutedEventArgs e) =>
        LaunchRepository(RepositoryLaunchTarget.VisualStudioCode);

    private void OnOpenVisualStudioSolutionClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string solutionPath })
        {
            LaunchRepository(RepositoryLaunchTarget.VisualStudio, solutionPath);
        }
    }

    private void OnOpenFileExplorerClick(object sender, RoutedEventArgs e) =>
        LaunchRepository(RepositoryLaunchTarget.FileExplorer);

    private async void OnCopyRepositoryPathClick(object sender, RoutedEventArgs e)
    {
        if (_repository is not null)
        {
            await CopyTextAsync(_repository.RootPath, "Repository path copied");
        }
    }

    private async void OnCopyCommitIdClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCommitDetails is not null)
        {
            await CopyTextAsync(
                _selectedCommitDetails.Commit.Hash,
                "Commit ID copied");
        }
    }

    private async void OnCopyCommitMessageClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCommitDetails is not null)
        {
            await CopyTextAsync(
                _selectedCommitDetails.Message,
                "Commit message copied");
        }
    }

    private async void OnCopyCommitAuthorClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCommitDetails is not null)
        {
            var commit = _selectedCommitDetails.Commit;
            await CopyTextAsync(
                $"{commit.AuthorName} <{commit.AuthorEmail}>",
                "Commit author copied");
        }
    }

    private async void OnCopyAllCommitDetailsClick(object sender, RoutedEventArgs e)
    {
        if (_selectedCommitDetails is not null)
        {
            await CopyTextAsync(
                FormatCommitDetails(_selectedCommitDetails),
                "Commit details copied");
        }
    }

    private async void OnFetchClick(object sender, RoutedEventArgs e) =>
        await RunRepositoryOperationAsync("fetch", "Fetching repository");

    private async void OnPullClick(object sender, RoutedEventArgs e) =>
        await RunRepositoryOperationAsync("pull", "Pulling repository");

    private async void OnPushClick(object sender, RoutedEventArgs e) =>
        await RunRepositoryOperationAsync("push", "Pushing commits");

    private async void OnRepositoryViewSelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        var showHistory = sender.SelectedItem == HistorySelectorItem;
        OverviewPanel.Visibility = showHistory
            ? Visibility.Collapsed
            : Visibility.Visible;
        HistoryPanel.Visibility = showHistory
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (showHistory && _repository is not null)
        {
            await LoadHistoryAsync();
        }
    }

    private async void OnRefreshHistoryClick(object sender, RoutedEventArgs e) =>
        await LoadHistoryAsync(forceRefresh: true);

    private async void OnHistorySelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (HistoryListView.SelectedItem is GitHistoryRowViewModel row)
        {
            await LoadCommitDetailsAsync(row);
        }
        else
        {
            ShowCommitDetailsPlaceholder("Select a commit dot to inspect its details.");
        }
    }

    private async Task RunRepositoryOperationAsync(string operationKey, string title)
    {
        if (_repository is null)
        {
            return;
        }

        RepositoryInfoBar.IsOpen = false;
        var options = new RepositoryOperationOptions(
            IncludeSubmodulesCheckBox.IsChecked == true);
        var result = await App.Current.Services.UserOperations.RunAsync(
            title,
            XamlRoot,
            (progress, cancellationToken) => App.Current.Services.RepositoryOperations.ExecuteAsync(
                operationKey,
                _repository.RootPath,
                options,
                progress,
                cancellationToken));

        if (result.IsCancelled)
        {
            RepositoryInfoBar.Severity = result.HasCancellationWarning
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Informational;
            RepositoryInfoBar.Title = result.HasCancellationWarning
                ? "Cancelled with a cleanup warning"
                : "Operation cancelled";
            RepositoryInfoBar.Message = result.Summary;
            RepositoryInfoBar.IsOpen = true;
        }
        else if (result.IsSuccess)
        {
            RepositoryInfoBar.Severity = InfoBarSeverity.Success;
            RepositoryInfoBar.Title = "Operation complete";
            RepositoryInfoBar.Message = result.Summary;
            RepositoryInfoBar.IsOpen = true;
            await RefreshRepositoryAsync(
                _repository.RootPath,
                keepSuccessMessage: true,
                recordRecentRepository: false);

            if (operationKey is "fetch" or "pull")
            {
                InvalidateHistory("History changed. Refreshing the graph…");
                if (RepositorySelectorBar.SelectedItem == HistorySelectorItem)
                {
                    await LoadHistoryAsync(forceRefresh: true);
                }
            }
        }
    }

    private async Task RefreshRepositoryAsync(
        string path,
        bool keepSuccessMessage = false,
        bool recordRecentRepository = true)
    {
        var previousRoot = _repository?.RootPath;
        var inspection = await App.Current.Services.GitClient.InspectRepositoryAsync(
            path,
            CancellationToken.None);

        if (!inspection.IsGitRepository || inspection.Repository is null)
        {
            _repository = null;
            InvalidateHistory("Select a Git repository to load its history.");
            CancelLauncherDiscovery();
            OpenWithButton.IsEnabled = false;
            RepositoryDetailsCard.Visibility = Visibility.Collapsed;
            RepositoryInfoBar.Severity = InfoBarSeverity.Error;
            RepositoryInfoBar.Title = "Not a Git repository";
            RepositoryInfoBar.Message = inspection.ErrorMessage;
            RepositoryInfoBar.IsOpen = true;
            return;
        }

        _repository = inspection.Repository;
        var repositoryChanged = !string.Equals(
            previousRoot,
            _repository.RootPath,
            StringComparison.OrdinalIgnoreCase);
        if (repositoryChanged)
        {
            InvalidateHistory("Open History to load the repository graph.");
            CancelLauncherDiscovery();
        }

        RepositoryPathTextBox.Text = _repository.RootPath;
        RepositoryNameText.Text = new DirectoryInfo(_repository.RootPath).Name;
        RepositoryRootText.Text = _repository.RootPath;
        BranchText.Text = _repository.Branch;
        WorkingTreeText.Text = _repository.IsClean
            ? "Clean"
            : $"{_repository.ChangedFileCount} changed file(s)";
        RemoteText.Text = _repository.RemoteUrl;
        OpenWithButton.IsEnabled = true;
        RepositoryDetailsCard.Visibility = Visibility.Visible;

        if (!keepSuccessMessage)
        {
            RepositoryInfoBar.IsOpen = false;
        }

        if (recordRecentRepository)
        {
            try
            {
                await App.Current.Services.RecentRepositories.RecordAsync(
                    _repository.RootPath);
            }
            catch (Exception exception)
            {
                await App.Current.Services.Logger.ErrorAsync(
                    $"Could not save recent repository '{_repository.RootPath}'.",
                    exception);
                ShowRepositoryMessage(
                    InfoBarSeverity.Warning,
                    "Repository opened with a warning",
                    "GitTool could not save this repository to the recent list.");
            }
        }

        if (repositoryChanged || _launcherAvailability is null)
        {
            _ = DiscoverRepositoryLaunchersAsync(_repository.RootPath);
        }

        if (repositoryChanged
            && RepositorySelectorBar.SelectedItem == HistorySelectorItem)
        {
            await LoadHistoryAsync();
        }
    }

    private async Task LoadHistoryAsync(bool forceRefresh = false)
    {
        if (_repository is null)
        {
            return;
        }

        var repositoryRoot = _repository.RootPath;
        if (!forceRefresh
            && _historyLoaded
            && string.Equals(
                _loadedHistoryRoot,
                repositoryRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _historyCancellation?.Cancel();
        _commitDetailsCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _historyCancellation = cancellation;
        var requestVersion = ++_historyRequestVersion;
        ++_commitDetailsRequestVersion;

        HistoryProgressRing.IsActive = true;
        HistoryProgressRing.Visibility = Visibility.Visible;
        HistoryRefreshButton.IsEnabled = false;
        HistoryListView.IsEnabled = false;
        if (HistoryListView.ItemsSource is null)
        {
            HistoryEmptyText.Text = "Loading commit history…";
            HistoryEmptyPanel.Visibility = Visibility.Visible;
        }

        try
        {
            var result = await App.Current.Services.GitClient.GetHistoryAsync(
                repositoryRoot,
                cancellation.Token);
            if (!IsCurrentHistoryRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                return;
            }

            if (result.IsCancelled)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                _historyLoaded = false;
                HistoryListView.ItemsSource = null;
                HistoryEmptyText.Text = "History could not be loaded.";
                HistoryEmptyPanel.Visibility = Visibility.Visible;
                ShowCommitDetailsPlaceholder("Commit details are unavailable until history reloads.");
                ShowHistoryError("History unavailable", result.ErrorMessage, result.Diagnostics);
                return;
            }

            var rows = GitHistoryGraphBuilder.Build(result.Commits)
                .Select(row => new GitHistoryRowViewModel(row))
                .ToArray();
            HistoryListView.ItemsSource = rows;
            _historyLoaded = true;
            _loadedHistoryRoot = repositoryRoot;

            if (rows.Length == 0)
            {
                HistoryEmptyText.Text = "This repository does not have any commits yet.";
                HistoryEmptyPanel.Visibility = Visibility.Visible;
                ShowCommitDetailsPlaceholder("Commit details will appear after the first commit.");
            }
            else
            {
                HistoryEmptyPanel.Visibility = Visibility.Collapsed;
                HistoryListView.SelectedIndex = 0;
            }
        }
        finally
        {
            cancellation.Dispose();
            if (IsCurrentHistoryRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                _historyCancellation = null;
                HistoryProgressRing.IsActive = false;
                HistoryProgressRing.Visibility = Visibility.Collapsed;
                HistoryRefreshButton.IsEnabled = true;
                HistoryListView.IsEnabled = true;
            }
        }
    }

    private async Task LoadCommitDetailsAsync(GitHistoryRowViewModel row)
    {
        if (_repository is null)
        {
            return;
        }

        _commitDetailsCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _commitDetailsCancellation = cancellation;
        var requestVersion = ++_commitDetailsRequestVersion;
        var repositoryRoot = _repository.RootPath;

        _selectedCommitDetails = null;
        ShowCommitMetadata(row.Commit);
        CommitMessageText.Text = "Loading commit details…";
        CommitChangedFilesText.Text = "Changed files";
        CommitTotalAdditionsText.Text = string.Empty;
        CommitTotalDeletionsText.Text = string.Empty;
        CommitFilesList.ItemsSource = null;
        CopyCommitButton.IsEnabled = false;
        CommitDetailsProgressRing.IsActive = true;
        CommitDetailsProgressRing.Visibility = Visibility.Visible;

        try
        {
            var result = await App.Current.Services.GitClient.GetCommitDetailsAsync(
                repositoryRoot,
                row.Commit.Hash,
                cancellation.Token);
            if (!IsCurrentCommitDetailsRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                return;
            }

            if (result.IsCancelled)
            {
                return;
            }

            if (!result.IsSuccess || result.Details is null)
            {
                CommitMessageText.Text = "Commit details could not be loaded.";
                ShowHistoryError(
                    "Commit details unavailable",
                    result.ErrorMessage,
                    result.Diagnostics);
                return;
            }

            var details = result.Details;
            _selectedCommitDetails = details;
            ShowCommitMetadata(details.Commit);
            CommitMessageText.Text = string.IsNullOrWhiteSpace(details.Message)
                ? "(No commit message)"
                : details.Message;
            CommitFilesList.ItemsSource = details.Files
                .Select(file => new GitCommitFileViewModel(file))
                .ToArray();
            CommitChangedFilesText.Text = $"{details.Files.Count} changed file(s)";
            CommitTotalAdditionsText.Text = $"+{details.TotalAdditions}";
            CommitTotalDeletionsText.Text = $"−{details.TotalDeletions}";
            CopyCommitButton.IsEnabled = true;
        }
        finally
        {
            cancellation.Dispose();
            if (IsCurrentCommitDetailsRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                _commitDetailsCancellation = null;
                CommitDetailsProgressRing.IsActive = false;
                CommitDetailsProgressRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowCommitMetadata(GitCommitInfo commit)
    {
        CommitDetailsPlaceholder.Visibility = Visibility.Collapsed;
        CommitDetailsScrollViewer.Visibility = Visibility.Visible;
        CommitSubjectText.Text = commit.Subject;
        CommitHashText.Text = commit.Hash;
        CommitAuthorText.Text = $"{commit.AuthorName} <{commit.AuthorEmail}>";
        CommitDateText.Text = commit.AuthorDate.ToLocalTime().ToString("F");
        CommitParentsText.Text = commit.ParentHashes.Count == 0
            ? "Root commit"
            : string.Join("  ", commit.ParentHashes.Select(AbbreviateHash));
        CommitReferencesText.Text = commit.References.Count == 0
            ? "None"
            : string.Join("  •  ", commit.References);
        CommitSignatureText.Text = commit.SignatureStatus switch
        {
            CommitSignatureStatus.Signed => "Signed",
            CommitSignatureStatus.Unsigned => "Unsigned",
            _ => "Unknown"
        };
        ToolTipService.SetToolTip(
            CommitSignatureBadge,
            commit.SignatureStatus switch
            {
                CommitSignatureStatus.Signed =>
                    "A signature is present. Verification is not evaluated.",
                CommitSignatureStatus.Unsigned =>
                    "No commit signature is present.",
                _ => "Signature presence could not be inspected."
            });
    }

    private void ShowCommitDetailsPlaceholder(string message)
    {
        _commitDetailsCancellation?.Cancel();
        _commitDetailsCancellation = null;
        _selectedCommitDetails = null;
        ++_commitDetailsRequestVersion;
        CommitDetailsPlaceholderText.Text = message;
        CommitDetailsPlaceholder.Visibility = Visibility.Visible;
        CommitDetailsScrollViewer.Visibility = Visibility.Collapsed;
        CommitDetailsProgressRing.IsActive = false;
        CommitDetailsProgressRing.Visibility = Visibility.Collapsed;
        CopyCommitButton.IsEnabled = false;
    }

    private void InvalidateHistory(string emptyMessage)
    {
        CancelPendingHistoryRequests();
        _historyLoaded = false;
        _loadedHistoryRoot = null;
        HistoryListView.ItemsSource = null;
        HistoryEmptyText.Text = emptyMessage;
        HistoryEmptyPanel.Visibility = Visibility.Visible;
        ShowCommitDetailsPlaceholder("Select a commit dot to inspect its details.");
    }

    private void CancelPendingHistoryRequests()
    {
        _historyCancellation?.Cancel();
        _commitDetailsCancellation?.Cancel();
        _historyCancellation = null;
        _commitDetailsCancellation = null;
        ++_historyRequestVersion;
        ++_commitDetailsRequestVersion;
        HistoryProgressRing.IsActive = false;
        HistoryProgressRing.Visibility = Visibility.Collapsed;
        HistoryRefreshButton.IsEnabled = true;
        HistoryListView.IsEnabled = true;
        CommitDetailsProgressRing.IsActive = false;
        CommitDetailsProgressRing.Visibility = Visibility.Collapsed;
    }

    private bool IsCurrentHistoryRequest(
        CancellationTokenSource cancellation,
        long requestVersion,
        string repositoryRoot) =>
        ReferenceEquals(_historyCancellation, cancellation)
        && requestVersion == _historyRequestVersion
        && string.Equals(
            _repository?.RootPath,
            repositoryRoot,
            StringComparison.OrdinalIgnoreCase);

    private bool IsCurrentCommitDetailsRequest(
        CancellationTokenSource cancellation,
        long requestVersion,
        string repositoryRoot) =>
        ReferenceEquals(_commitDetailsCancellation, cancellation)
        && requestVersion == _commitDetailsRequestVersion
        && string.Equals(
            _repository?.RootPath,
            repositoryRoot,
            StringComparison.OrdinalIgnoreCase);

    private void OnRecentRepositoriesChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshRecentRepositories();
        }
        else
        {
            DispatcherQueue.TryEnqueue(RefreshRecentRepositories);
        }
    }

    private void RefreshRecentRepositories()
    {
        var repositories = App.Current.Services.RecentRepositories.Entries
            .Select(entry => new RecentRepositoryViewModel(entry))
            .ToArray();
        RecentRepositoriesList.ItemsSource = repositories;
        RecentRepositoriesList.Visibility = repositories.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        RecentRepositoriesEmptyText.Visibility = repositories.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task DiscoverRepositoryLaunchersAsync(string repositoryRoot)
    {
        _launcherCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _launcherCancellation = cancellation;
        var requestVersion = ++_launcherRequestVersion;

        _launcherAvailability = null;
        OpenTerminalMenuItem.IsEnabled = false;
        OpenVisualStudioCodeMenuItem.IsEnabled = false;
        OpenVisualStudioMenuItem.IsEnabled = false;
        OpenVisualStudioMenuItem.Items.Clear();

        try
        {
            var availability = await App.Current.Services.RepositoryLauncher.DiscoverAsync(
                repositoryRoot,
                cancellation.Token);
            if (!IsCurrentLauncherRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                return;
            }

            _launcherAvailability = availability;
            OpenTerminalMenuItem.IsEnabled = availability.CanOpenTerminal;
            OpenVisualStudioCodeMenuItem.IsEnabled =
                availability.CanOpenVisualStudioCode;
            OpenVisualStudioMenuItem.IsEnabled =
                availability.CanOpenVisualStudio;

            foreach (var solutionPath in availability.VisualStudioSolutions)
            {
                var item = new MenuFlyoutItem
                {
                    Text = Path.GetRelativePath(repositoryRoot, solutionPath),
                    Tag = solutionPath
                };
                item.Click += OnOpenVisualStudioSolutionClick;
                OpenVisualStudioMenuItem.Items.Add(item);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentLauncherRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                await App.Current.Services.Logger.ErrorAsync(
                    $"Could not discover repository launchers for '{repositoryRoot}'.",
                    exception);
                ShowRepositoryMessage(
                    InfoBarSeverity.Warning,
                    "Some applications could not be detected",
                    "File Explorer and Copy path remain available.");
            }
        }
        finally
        {
            if (IsCurrentLauncherRequest(
                    cancellation,
                    requestVersion,
                    repositoryRoot))
            {
                _launcherCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentLauncherRequest(
        CancellationTokenSource cancellation,
        long requestVersion,
        string repositoryRoot) =>
        ReferenceEquals(_launcherCancellation, cancellation)
        && requestVersion == _launcherRequestVersion
        && string.Equals(
            _repository?.RootPath,
            repositoryRoot,
            StringComparison.OrdinalIgnoreCase);

    private void CancelLauncherDiscovery()
    {
        _launcherCancellation?.Cancel();
        _launcherCancellation = null;
        _launcherAvailability = null;
        ++_launcherRequestVersion;
        OpenTerminalMenuItem.IsEnabled = false;
        OpenVisualStudioCodeMenuItem.IsEnabled = false;
        OpenVisualStudioMenuItem.IsEnabled = false;
        OpenVisualStudioMenuItem.Items.Clear();
    }

    private void LaunchRepository(
        RepositoryLaunchTarget target,
        string? solutionPath = null)
    {
        if (_repository is null)
        {
            return;
        }

        var availability = _launcherAvailability
            ?? new RepositoryLauncherAvailability(null, null, null, []);
        try
        {
            App.Current.Services.RepositoryLauncher.Launch(
                target,
                _repository.RootPath,
                availability,
                solutionPath);
        }
        catch (Exception exception)
        {
            ShowRepositoryMessage(
                InfoBarSeverity.Error,
                "Could not open repository",
                exception.Message);
        }
    }

    private async Task CopyTextAsync(string text, string successTitle)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            ShowRepositoryMessage(
                InfoBarSeverity.Informational,
                successTitle,
                "The requested text is on the clipboard.");
        }
        catch (Exception exception)
        {
            await App.Current.Services.Logger.ErrorAsync(
                "Could not copy repository information to the clipboard.",
                exception);
            ShowRepositoryMessage(
                InfoBarSeverity.Error,
                "Could not copy",
                exception.Message);
        }
    }

    private static string FormatCommitDetails(GitCommitDetails details)
    {
        var commit = details.Commit;
        var text = new StringBuilder();
        var signatureStatus = commit.SignatureStatus switch
        {
            CommitSignatureStatus.Signed => "Signed (presence only)",
            CommitSignatureStatus.Unsigned => "Unsigned",
            _ => "Unknown"
        };
        text.AppendLine($"Commit: {commit.Hash}");
        text.AppendLine($"Signature: {signatureStatus}");
        text.AppendLine($"Author: {commit.AuthorName} <{commit.AuthorEmail}>");
        text.AppendLine($"Date: {commit.AuthorDate.ToLocalTime():F}");
        text.AppendLine(
            $"Parents: {(commit.ParentHashes.Count == 0 ? "Root commit" : string.Join(" ", commit.ParentHashes))}");
        text.AppendLine(
            $"References: {(commit.References.Count == 0 ? "None" : string.Join(", ", commit.References))}");
        text.AppendLine();
        text.AppendLine("Message:");
        text.AppendLine(details.Message);
        text.AppendLine();
        text.AppendLine(
            $"Changes: {details.Files.Count} file(s), +{details.TotalAdditions}, −{details.TotalDeletions}");
        foreach (var file in details.Files)
        {
            var changes = file.Additions is null || file.Deletions is null
                ? "Binary"
                : $"+{file.Additions} −{file.Deletions}";
            text.AppendLine($"{file.Path}  {changes}");
        }

        return text.ToString().TrimEnd();
    }

    private static void OnNestedScrollPointerWheelChanged(
        object sender,
        PointerRoutedEventArgs args) =>
        args.Handled = true;

    private void ShowRepositoryMessage(
        InfoBarSeverity severity,
        string title,
        string message)
    {
        RepositoryInfoBar.Severity = severity;
        RepositoryInfoBar.Title = title;
        RepositoryInfoBar.Message = message;
        RepositoryInfoBar.IsOpen = true;
    }

    private void ShowHistoryError(string title, string error, string diagnostics)
    {
        RepositoryInfoBar.Severity = InfoBarSeverity.Error;
        RepositoryInfoBar.Title = title;
        RepositoryInfoBar.Message = string.IsNullOrWhiteSpace(diagnostics)
            ? error
            : $"{error}\n{diagnostics.Trim()}";
        RepositoryInfoBar.IsOpen = true;
    }

    private static string AbbreviateHash(string hash) =>
        hash[..Math.Min(12, hash.Length)];
}
