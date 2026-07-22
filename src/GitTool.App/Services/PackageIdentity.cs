using System.Runtime.InteropServices;

namespace GitTool.App.Services;

internal static class PackageIdentity
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
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
