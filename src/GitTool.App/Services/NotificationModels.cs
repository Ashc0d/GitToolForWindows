namespace GitTool.App.Services;

internal enum NotificationSystemSetting
{
    Enabled,
    DisabledForApplication,
    DisabledForUser,
    DisabledByGroupPolicy,
    DisabledByManifest,
    Unsupported
}

internal enum NotificationCapabilityStatus
{
    Available,
    Unsupported,
    Elevated,
    RegistrationFailed,
    DisabledForApplication,
    DisabledForUser,
    DisabledByGroupPolicy,
    DisabledByManifest
}

internal readonly record struct NotificationCapability(
    NotificationCapabilityStatus Status,
    string Message)
{
    public bool CanSend => Status == NotificationCapabilityStatus.Available;
}

internal enum NotificationDeliveryStatus
{
    Sent,
    DisabledByGitTool,
    SuppressedWhileForeground,
    SuppressedForShutdown,
    Unsupported,
    Elevated,
    RegistrationFailed,
    DisabledForApplication,
    DisabledForUser,
    DisabledByGroupPolicy,
    DisabledByManifest,
    SendFailed
}

internal readonly record struct NotificationDeliveryResult(
    NotificationDeliveryStatus Status,
    string Message)
{
    public bool WasSent => Status == NotificationDeliveryStatus.Sent;
}
