using System.Runtime.InteropServices;
using System.Xml.Linq;
using Windows.ApplicationModel;

namespace GitTool.App.Services;

internal static class PackageIdentity
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged { get; } = Detect();

    public static bool HasNotificationComActivator { get; } =
        DetectNotificationComActivator();

    private static bool Detect()
    {
        uint packageFullNameLength = 0;
        var result = GetCurrentPackageFullName(ref packageFullNameLength, 0);
        return result is ErrorSuccess or ErrorInsufficientBuffer;
    }

    private static bool DetectNotificationComActivator()
    {
        if (!IsPackaged)
        {
            return false;
        }

        try
        {
            var manifestPath = Path.Combine(
                Package.Current.InstalledLocation.Path,
                "AppxManifest.xml");
            return ManifestDeclaresNotificationComActivator(manifestPath);
        }
        catch
        {
            return false;
        }
    }

    internal static bool ManifestDeclaresNotificationComActivator(string manifestPath)
    {
        var manifest = XDocument.Load(manifestPath, LoadOptions.None);
        var extensions = manifest
            .Descendants()
            .Where(element => element.Name.LocalName == "Extension")
            .Select(element => (string?)element.Attribute("Category"))
            .ToHashSet(StringComparer.Ordinal);

        return extensions.Contains("windows.comServer")
            && extensions.Contains("windows.toastNotificationActivation");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        nint packageFullName);
}
