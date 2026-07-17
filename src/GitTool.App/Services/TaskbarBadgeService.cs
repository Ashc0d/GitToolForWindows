using Microsoft.Windows.BadgeNotifications;
using System.Runtime.InteropServices;

namespace GitTool.App.Services;

public sealed class TaskbarBadgeService
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly bool HasPackageIdentity = DetectPackageIdentity();

    public void ShowActivity()
    {
        if (HasPackageIdentity)
        {
            TrySet(BadgeNotificationGlyph.Activity);
        }
    }

    public void ShowError()
    {
        if (HasPackageIdentity)
        {
            TrySet(BadgeNotificationGlyph.Error);
        }
    }

    public void Clear()
    {
        if (!HasPackageIdentity)
        {
            return;
        }

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

    private static bool DetectPackageIdentity()
    {
        uint packageFullNameLength = 0;
        var result = GetCurrentPackageFullName(ref packageFullNameLength, 0);
        return result is ErrorSuccess or ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        nint packageFullName);
}
