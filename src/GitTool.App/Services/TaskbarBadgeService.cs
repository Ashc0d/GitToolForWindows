using Microsoft.Windows.BadgeNotifications;

namespace GitTool.App.Services;

public sealed class TaskbarBadgeService
{
    public void ShowActivity() => TrySet(BadgeNotificationGlyph.Activity);

    public void ShowError() => TrySet(BadgeNotificationGlyph.Error);

    public void Clear()
    {
        try
        {
            BadgeNotificationManager.Current.ClearBadge();
        }
        catch
        {
            // Badges require a supported shell and package identity.
        }
    }

    private static void TrySet(BadgeNotificationGlyph glyph)
    {
        try
        {
            BadgeNotificationManager.Current.SetBadgeAsGlyph(glyph);
        }
        catch
        {
            // The in-app status remains available when taskbar badges are unsupported.
        }
    }
}
