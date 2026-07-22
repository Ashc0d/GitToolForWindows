using System.Diagnostics;
using GitTool.App.Services;
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
        RefreshNotificationCapability();
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

    private async void OnTestNotificationClick(object sender, RoutedEventArgs e)
    {
        if (!NotificationsToggleSwitch.IsOn)
        {
            ShowInfo(
                InfoBarSeverity.Informational,
                "Notifications are silenced",
                "Enable Windows notifications before testing.");
            return;
        }

        TestNotificationButton.IsEnabled = false;
        var result = await App.Current.Services.NotificationService.ShowTestAsync(
            "GitTool notification test",
            "Notifications are ready for Git operations that finish while GitTool is in the background.");
        TestNotificationButton.IsEnabled = true;

        var severity = result.Status switch
        {
            NotificationDeliveryStatus.Sent => InfoBarSeverity.Success,
            NotificationDeliveryStatus.SendFailed
                or NotificationDeliveryStatus.RegistrationFailed => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Warning
        };
        var title = result.WasSent ? "Test notification sent" : "Test notification not sent";
        ShowInfo(severity, title, result.Message);
        RefreshNotificationCapability();
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

    private void RefreshNotificationCapability()
    {
        var capability = App.Current.Services.NotificationService.GetCapability();
        NotificationCapabilityInfoBar.Severity = capability.Status switch
        {
            NotificationCapabilityStatus.Available => InfoBarSeverity.Success,
            NotificationCapabilityStatus.RegistrationFailed => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Warning
        };
        NotificationCapabilityInfoBar.Title = capability.CanSend
            ? "Windows notifications available"
            : "Windows notifications unavailable";
        NotificationCapabilityInfoBar.Message = capability.Message;
        NotificationCapabilityInfoBar.IsOpen = true;
    }
}
