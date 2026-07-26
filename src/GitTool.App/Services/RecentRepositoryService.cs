using GitTool.Core.Infrastructure;
using GitTool.Core.Models;

namespace GitTool.App.Services;

internal sealed class RecentRepositoryService
{
    internal const int MaximumEntries = 10;

    private readonly AppSettings _settings;
    private readonly JsonSettingsStore _settingsStore;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _entriesLock = new();

    public RecentRepositoryService(
        AppSettings settings,
        JsonSettingsStore settingsStore,
        Func<DateTimeOffset>? utcNow = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public event EventHandler? RepositoriesChanged;

    public IReadOnlyList<RecentRepositoryEntry> Entries
    {
        get
        {
            lock (_entriesLock)
            {
                return _settings.RecentRepositories.ToArray();
            }
        }
    }

    public async Task RecordAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(repositoryPath);
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            lock (_entriesLock)
            {
                _settings.RecentRepositories.RemoveAll(entry =>
                    PathsEqual(entry.Path, normalizedPath));
                _settings.RecentRepositories.Insert(
                    0,
                    new RecentRepositoryEntry(normalizedPath, _utcNow()));
                if (_settings.RecentRepositories.Count > MaximumEntries)
                {
                    _settings.RecentRepositories.RemoveRange(
                        MaximumEntries,
                        _settings.RecentRepositories.Count - MaximumEntries);
                }
            }

            await _settingsStore.SaveAsync(_settings, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }

        RepositoriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(repositoryPath);
        var removed = false;

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            lock (_entriesLock)
            {
                removed = _settings.RecentRepositories.RemoveAll(entry =>
                    PathsEqual(entry.Path, normalizedPath)) > 0;
            }

            if (removed)
            {
                await _settingsStore.SaveAsync(_settings, cancellationToken);
            }
        }
        finally
        {
            _saveGate.Release();
        }

        if (removed)
        {
            RepositoriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
