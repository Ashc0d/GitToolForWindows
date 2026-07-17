using GitTool.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitTool.App.Views;

public sealed partial class ClonePage : Page
{
    public ClonePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DestinationTextBox.Text))
        {
            DestinationTextBox.Text = App.Current.Services.Settings.DefaultCloneDirectory;
        }
    }

    private async void OnChooseDestinationClick(object sender, RoutedEventArgs e)
    {
        var selectedPath = await App.Current.Services.FolderPicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DestinationTextBox.Text = selectedPath;
        }
    }

    private async void OnCloneClick(object sender, RoutedEventArgs e)
    {
        ResultInfoBar.IsOpen = false;

        var request = new GitCloneRequest(
            RepositoryUrlTextBox.Text,
            DestinationTextBox.Text,
            SubmodulesCheckBox.IsChecked == true);

        var result = await App.Current.Services.UserOperations.RunAsync(
            "Cloning repository",
            XamlRoot,
            (progress, cancellationToken) => App.Current.Services.GitClient.CloneAsync(
                request,
                progress,
                cancellationToken));

        if (result.IsSuccess)
        {
            ResultInfoBar.Severity = InfoBarSeverity.Success;
            ResultInfoBar.Title = "Clone complete";
            ResultInfoBar.Message = result.Summary;
            ResultInfoBar.IsOpen = true;
        }
    }
}
