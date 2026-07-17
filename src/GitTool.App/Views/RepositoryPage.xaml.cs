using GitTool.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitTool.App.Views;

public sealed partial class RepositoryPage : Page
{
    private GitRepositoryInfo? _repository;

    public RepositoryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
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
            await RefreshRepositoryAsync(RepositoryPathTextBox.Text);
        }
    }

    private async void OnFetchClick(object sender, RoutedEventArgs e) =>
        await RunRepositoryOperationAsync("fetch", "Fetching repository");

    private async void OnPullClick(object sender, RoutedEventArgs e) =>
        await RunRepositoryOperationAsync("pull", "Pulling repository");

    private async void OnPushClick(object sender, RoutedEventArgs e) =>
        await RunRepositoryOperationAsync("push", "Pushing commits");

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

        if (result.IsSuccess)
        {
            RepositoryInfoBar.Severity = InfoBarSeverity.Success;
            RepositoryInfoBar.Title = "Operation complete";
            RepositoryInfoBar.Message = result.Summary;
            RepositoryInfoBar.IsOpen = true;
            await RefreshRepositoryAsync(_repository.RootPath, keepSuccessMessage: true);
        }
    }

    private async Task RefreshRepositoryAsync(string path, bool keepSuccessMessage = false)
    {
        var inspection = await App.Current.Services.GitClient.InspectRepositoryAsync(
            path,
            CancellationToken.None);

        if (!inspection.IsGitRepository || inspection.Repository is null)
        {
            _repository = null;
            RepositoryDetailsCard.Visibility = Visibility.Collapsed;
            RepositoryInfoBar.Severity = InfoBarSeverity.Error;
            RepositoryInfoBar.Title = "Not a Git repository";
            RepositoryInfoBar.Message = inspection.ErrorMessage;
            RepositoryInfoBar.IsOpen = true;
            return;
        }

        _repository = inspection.Repository;
        RepositoryPathTextBox.Text = _repository.RootPath;
        RepositoryNameText.Text = new DirectoryInfo(_repository.RootPath).Name;
        RepositoryRootText.Text = _repository.RootPath;
        BranchText.Text = _repository.Branch;
        WorkingTreeText.Text = _repository.IsClean
            ? "Clean"
            : $"{_repository.ChangedFileCount} changed file(s)";
        RemoteText.Text = _repository.RemoteUrl;
        RepositoryDetailsCard.Visibility = Visibility.Visible;

        if (!keepSuccessMessage)
        {
            RepositoryInfoBar.IsOpen = false;
        }
    }
}
