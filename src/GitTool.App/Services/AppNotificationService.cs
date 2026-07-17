using GitTool.Core.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GitTool.App.Services;

public sealed class AppNotificationService
{
    private readonly Func<AppSettings> _settings;

    public AppNotificationService(Func<AppSettings> settings)
    {
        _settings = settings;
    }

    public void ShowAttention(string title, string message)
    {
        if (!_settings().NotificationsEnabled)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(Truncate(message, 180))
                .BuildNotification();
            notification.ExpiresOnReboot = true;
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // A ContentDialog is always shown in-app, even if notifications are unavailable.
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";
}
