using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GitTool.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var services = App.Current.Services;
        DefaultCloneDirectoryTextBox.Text = services.Settings.DefaultCloneDirectory;
        NotificationsToggleSwitch.IsOn = services.Settings.NotificationsEnabled;
        LogsDirectoryTextBox.Text = services.Paths.LogsRoot;
    }

    private async void OnChooseDefaultDirectoryClick(object sender, RoutedEventArgs e)
    {
        var selectedPath = await App.Current.Services.FolderPicker.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DefaultCloneDirectoryTextBox.Text = selectedPath;
        }
    }

    private async void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var services = App.Current.Services;
        services.Settings.DefaultCloneDirectory = DefaultCloneDirectoryTextBox.Text;
        services.Settings.NotificationsEnabled = NotificationsToggleSwitch.IsOn;

        try
        {
            await services.SettingsStore.SaveAsync(services.Settings);
            services.Logger.Info("Application settings saved.");
            ShowInfo(InfoBarSeverity.Success, "Settings saved", "Your defaults are now active.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await services.Logger.ErrorAsync("Settings could not be saved.", exception);
            ShowInfo(InfoBarSeverity.Error, "Settings were not saved", exception.Message);
        }
    }

    private void OnTestNotificationClick(object sender, RoutedEventArgs e)
    {
        if (!NotificationsToggleSwitch.IsOn)
        {
            ShowInfo(
                InfoBarSeverity.Informational,
                "Notifications are silenced",
                "Enable Windows notifications and save settings before testing.");
            return;
        }

        App.Current.Services.Settings.NotificationsEnabled = true;
        App.Current.Services.NotificationService.ShowAttention(
            "GitTool notification test",
            "Notifications are ready for errors and operations that need attention.");
        ShowInfo(InfoBarSeverity.Success, "Test sent", "Check Windows Notification Center.");
    }

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(App.Current.Services.Paths.LogsRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add(App.Current.Services.Paths.LogsRoot);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowInfo(InfoBarSeverity.Error, "Could not open logs", exception.Message);
        }
    }

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.Title = title;
        SettingsInfoBar.Message = message;
        SettingsInfoBar.IsOpen = true;
    }
}
