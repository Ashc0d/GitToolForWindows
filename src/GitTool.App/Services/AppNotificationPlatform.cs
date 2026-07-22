using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GitTool.App.Services;

internal interface IAppNotificationPlatform
{
    bool IsSupported();

    NotificationSystemSetting GetSetting();

    void Register(Action<string> notificationInvoked);

    void Show(string title, string message);

    void Unregister();
}

internal sealed class WindowsAppNotificationPlatform : IAppNotificationPlatform
{
    private AppNotificationManager? _manager;
    private Action<string>? _notificationInvoked;

    public bool IsSupported() => AppNotificationManager.IsSupported();

    public NotificationSystemSetting GetSetting()
    {
        var setting = GetRegisteredManager().Setting;
        return setting switch
        {
            AppNotificationSetting.Enabled => NotificationSystemSetting.Enabled,
            AppNotificationSetting.DisabledForApplication =>
                NotificationSystemSetting.DisabledForApplication,
            AppNotificationSetting.DisabledForUser => NotificationSystemSetting.DisabledForUser,
            AppNotificationSetting.DisabledByGroupPolicy =>
                NotificationSystemSetting.DisabledByGroupPolicy,
            AppNotificationSetting.DisabledByManifest =>
                NotificationSystemSetting.DisabledByManifest,
            _ => NotificationSystemSetting.Unsupported
        };
    }

    public void Register(Action<string> notificationInvoked)
    {
        ArgumentNullException.ThrowIfNull(notificationInvoked);
        if (_manager is not null)
        {
            return;
        }

        var manager = AppNotificationManager.Default;
        _notificationInvoked = notificationInvoked;
        manager.NotificationInvoked += OnNotificationInvoked;

        try
        {
            manager.Register();
            _manager = manager;
        }
        catch
        {
            manager.NotificationInvoked -= OnNotificationInvoked;
            _notificationInvoked = null;
            throw;
        }
    }

    public void Show(string title, string message)
    {
        var notification = new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddText(title)
            .AddText(Truncate(message, 180))
            .BuildNotification();
        notification.ExpiresOnReboot = true;
        GetRegisteredManager().Show(notification);
    }

    public void Unregister()
    {
        var manager = _manager;
        _manager = null;

        if (manager is null)
        {
            return;
        }

        try
        {
            manager.Unregister();
        }
        finally
        {
            manager.NotificationInvoked -= OnNotificationInvoked;
            _notificationInvoked = null;
        }
    }

    private AppNotificationManager GetRegisteredManager() =>
        _manager ?? throw new InvalidOperationException(
            "The app notification manager has not been registered.");

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args) =>
        _notificationInvoked?.Invoke(args.Argument);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 1)] + "…";
}
