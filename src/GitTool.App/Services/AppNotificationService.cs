using System.Security.Principal;
using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

namespace GitTool.App.Services;

public sealed class AppNotificationService
{
    private readonly Func<AppSettings> _settings;
    private readonly IAppLogger _logger;
    private readonly IAppNotificationPlatform _platform;
    private readonly Func<bool> _isElevated;
    private readonly object _stateLock = new();
    private NotificationCapability _capability = new(
        NotificationCapabilityStatus.Unsupported,
        "Windows notification support has not been initialized.");
    private bool _initializationStarted;
    private bool _registered;
    private bool _isForeground;
    private bool _suppressForShutdown;

    public AppNotificationService(Func<AppSettings> settings, IAppLogger logger)
        : this(settings, logger, new WindowsAppNotificationPlatform(), IsCurrentProcessElevated)
    {
    }

    internal AppNotificationService(
        Func<AppSettings> settings,
        IAppLogger logger,
        IAppNotificationPlatform platform,
        Func<bool> isElevated)
    {
        _settings = settings;
        _logger = logger;
        _platform = platform;
        _isElevated = isElevated;
    }

    internal async Task InitializeAsync(Action<string> notificationInvoked)
    {
        lock (_stateLock)
        {
            if (_initializationStarted)
            {
                return;
            }

            _initializationStarted = true;
        }

        if (_isElevated())
        {
            SetCapability(new NotificationCapability(
                NotificationCapabilityStatus.Elevated,
                "Windows does not support app notifications from an elevated administrator process. Start GitTool normally."));
            _logger.Warning("Windows notifications are unavailable because GitTool is running elevated.");
            return;
        }

        try
        {
            if (!_platform.IsSupported())
            {
                SetCapability(CreateUnsupportedCapability());
                _logger.Warning(
                    "Windows app notifications are unsupported in this session. "
                    + "The Windows App Runtime Singleton broker may be unavailable.");
                return;
            }

            _platform.Register(notificationInvoked);
            lock (_stateLock)
            {
                _registered = true;
            }

            SetCapability(ReadSystemCapability());
            _logger.Info("Windows app notification manager registered.");
        }
        catch (Exception exception)
        {
            SetCapability(new NotificationCapability(
                NotificationCapabilityStatus.RegistrationFailed,
                "GitTool could not register with Windows notifications. See the application log for details."));
            await _logger.ErrorAsync(
                "Windows app notification registration failed.",
                exception);
        }
    }

    internal NotificationCapability GetCapability()
    {
        lock (_stateLock)
        {
            if (!_registered)
            {
                return _capability;
            }
        }

        try
        {
            var capability = ReadSystemCapability();
            SetCapability(capability);
            return capability;
        }
        catch (Exception exception)
        {
            _logger.Warning(
                $"Windows notification availability could not be read: {exception}");
            return new NotificationCapability(
                NotificationCapabilityStatus.RegistrationFailed,
                "GitTool could not read the current Windows notification setting. See the application log for details.");
        }
    }

    internal void SetForegroundState(bool isForeground)
    {
        lock (_stateLock)
        {
            _isForeground = isForeground;
        }
    }

    internal void SuppressForShutdown()
    {
        lock (_stateLock)
        {
            _suppressForShutdown = true;
        }
    }

    internal Task<NotificationDeliveryResult> ShowTestAsync(
        string title,
        string message) =>
        ShowCoreAsync(
            title,
            message,
            respectAppPreference: false,
            requireBackground: false);

    internal Task<NotificationDeliveryResult> ShowOperationCompletionAsync(
        string operationTitle,
        OperationResult result)
    {
        var notificationTitle = result switch
        {
            { HasCancellationWarning: true } => "GitTool needs attention",
            { IsCancelled: true } => $"{operationTitle} cancelled",
            { IsSuccess: true } => $"{operationTitle} completed",
            _ => "GitTool needs attention"
        };

        return ShowCoreAsync(
            notificationTitle,
            result.Summary,
            respectAppPreference: true,
            requireBackground: true);
    }

    internal void Shutdown()
    {
        lock (_stateLock)
        {
            if (!_registered)
            {
                return;
            }

            _registered = false;
        }

        try
        {
            _platform.Unregister();
            _logger.Info("Windows app notification manager unregistered.");
        }
        catch (Exception exception)
        {
            _logger.Warning($"Windows app notification cleanup failed: {exception}");
        }
    }

    private async Task<NotificationDeliveryResult> ShowCoreAsync(
        string title,
        string message,
        bool respectAppPreference,
        bool requireBackground)
    {
        lock (_stateLock)
        {
            if (_suppressForShutdown)
            {
                return new NotificationDeliveryResult(
                    NotificationDeliveryStatus.SuppressedForShutdown,
                    "The notification was skipped because GitTool is closing.");
            }

            if (requireBackground && _isForeground)
            {
                return new NotificationDeliveryResult(
                    NotificationDeliveryStatus.SuppressedWhileForeground,
                    "The notification was skipped because GitTool is already in the foreground.");
            }
        }

        if (respectAppPreference && !_settings().NotificationsEnabled)
        {
            return new NotificationDeliveryResult(
                NotificationDeliveryStatus.DisabledByGitTool,
                "Windows notifications are silenced in GitTool settings.");
        }

        var capability = GetCapability();
        if (!capability.CanSend)
        {
            return FromCapability(capability);
        }

        try
        {
            _platform.Show(title, message);
            return new NotificationDeliveryResult(
                NotificationDeliveryStatus.Sent,
                "Windows accepted the notification. If no banner appears, check Notification Center or Do Not Disturb.");
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync("Windows app notification delivery failed.", exception);
            return new NotificationDeliveryResult(
                NotificationDeliveryStatus.SendFailed,
                "Windows did not accept the notification. See the application log for details.");
        }
    }

    private NotificationCapability ReadSystemCapability() =>
        _platform.GetSetting() switch
        {
            NotificationSystemSetting.Enabled => new NotificationCapability(
                NotificationCapabilityStatus.Available,
                "Windows notification support is available."),
            NotificationSystemSetting.DisabledForApplication => new NotificationCapability(
                NotificationCapabilityStatus.DisabledForApplication,
                "Windows notifications are turned off specifically for GitTool in Windows Settings."),
            NotificationSystemSetting.DisabledForUser => new NotificationCapability(
                NotificationCapabilityStatus.DisabledForUser,
                "Windows notifications are turned off for this user."),
            NotificationSystemSetting.DisabledByGroupPolicy => new NotificationCapability(
                NotificationCapabilityStatus.DisabledByGroupPolicy,
                "Windows notifications are disabled by organizational policy."),
            NotificationSystemSetting.DisabledByManifest => new NotificationCapability(
                NotificationCapabilityStatus.DisabledByManifest,
                "The installed GitTool package does not permit Windows notifications."),
            _ => CreateUnsupportedCapability()
        };

    private void SetCapability(NotificationCapability capability)
    {
        lock (_stateLock)
        {
            _capability = capability;
        }
    }

    private static NotificationCapability CreateUnsupportedCapability() => new(
        NotificationCapabilityStatus.Unsupported,
        "Windows notification support is unavailable in this portable session. The Windows App Runtime notification broker may not be installed.");

    private static NotificationDeliveryResult FromCapability(
        NotificationCapability capability) => new(
            capability.Status switch
            {
                NotificationCapabilityStatus.Unsupported => NotificationDeliveryStatus.Unsupported,
                NotificationCapabilityStatus.Elevated => NotificationDeliveryStatus.Elevated,
                NotificationCapabilityStatus.RegistrationFailed =>
                    NotificationDeliveryStatus.RegistrationFailed,
                NotificationCapabilityStatus.DisabledForApplication =>
                    NotificationDeliveryStatus.DisabledForApplication,
                NotificationCapabilityStatus.DisabledForUser =>
                    NotificationDeliveryStatus.DisabledForUser,
                NotificationCapabilityStatus.DisabledByGroupPolicy =>
                    NotificationDeliveryStatus.DisabledByGroupPolicy,
                NotificationCapabilityStatus.DisabledByManifest =>
                    NotificationDeliveryStatus.DisabledByManifest,
                _ => NotificationDeliveryStatus.SendFailed
            },
            capability.Message);

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
