using System.Diagnostics;
using GitTool.Core.Infrastructure;
using Windows.Storage;

namespace GitTool.App.Services;

internal static class AppDataStorage
{
    private const string DataDirectoryName = "GitTool";

    public static string ResolveDataRoot()
    {
        if (!PackageIdentity.IsPackaged)
        {
            return AppPaths.DefaultDataRoot;
        }

        var packageDataRoot = Path.Combine(
            ApplicationData.Current.LocalFolder.Path,
            DataDirectoryName);

        MigrateLegacyData(AppPaths.DefaultDataRoot, packageDataRoot);
        return packageDataRoot;
    }

    internal static void MigrateLegacyData(string legacyRoot, string packageDataRoot)
    {
        if (!Directory.Exists(legacyRoot) || PathsReferToSameLocation(legacyRoot, packageDataRoot))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(packageDataRoot))
            {
                Directory.Move(legacyRoot, packageDataRoot);
                return;
            }

            MergeDirectory(legacyRoot, packageDataRoot);
            Directory.Delete(legacyRoot, false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"GitTool could not migrate legacy app data: {exception}");
        }
    }

    private static void MergeDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot))
        {
            var destinationFile = GetAvailableDestination(
                destinationRoot,
                Path.GetFileName(sourceFile));
            File.Move(sourceFile, destinationFile);
        }

        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourceRoot))
        {
            if ((File.GetAttributes(sourceDirectory) & System.IO.FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var destinationDirectory = Path.Combine(
                destinationRoot,
                Path.GetFileName(sourceDirectory));
            MergeDirectory(sourceDirectory, destinationDirectory);
            Directory.Delete(sourceDirectory, false);
        }
    }

    private static string GetAvailableDestination(string destinationRoot, string fileName)
    {
        var candidate = Path.Combine(destinationRoot, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(
            destinationRoot,
            $"{stem}-legacy-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}{extension}");
    }

    private static bool PathsReferToSameLocation(string first, string second) =>
        string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
