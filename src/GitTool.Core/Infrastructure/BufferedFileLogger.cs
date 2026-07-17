using System.Collections.Concurrent;
using System.Text;

namespace GitTool.Core.Infrastructure;

public sealed class BufferedFileLogger : IAppLogger, IAsyncDisposable
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);

    private readonly AppPaths _paths;
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _periodicFlushTask;

    public BufferedFileLogger(AppPaths paths)
    {
        _paths = paths;
        _periodicFlushTask = RunPeriodicFlushAsync(_shutdown.Token);
        Info("GitTool started.");
    }

    public void Info(string message) => Enqueue("INFO", message);

    public void Warning(string message) => Enqueue("WARN", message);

    public async Task ErrorAsync(string message, Exception? exception = null)
    {
        var detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
        Enqueue("ERROR", detail);
        await FlushAsync().ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_entries.IsEmpty)
            {
                return;
            }

            var pending = new List<string>();
            while (_entries.TryDequeue(out var entry))
            {
                pending.Add(entry);
            }

            try
            {
                Directory.CreateDirectory(_paths.LogsRoot);
                var logFile = Path.Combine(_paths.LogsRoot, $"GitTool-{DateTime.Now:yyyyMMdd}.log");
                await File.AppendAllLinesAsync(logFile, pending, Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                RotateOldLogs();
            }
            catch
            {
                foreach (var entry in pending)
                {
                    _entries.Enqueue(entry);
                }

                throw;
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Info("GitTool is closing.");
        _shutdown.Cancel();

        try
        {
            await _periodicFlushTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await FlushAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _flushLock.Dispose();
    }

    private void Enqueue(string level, string message)
    {
        _entries.Enqueue($"{DateTimeOffset.Now:O} [{level}] {message}");
    }

    private async Task RunPeriodicFlushAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Preserve queued entries and try again at the next interval.
            }
        }
    }

    private void RotateOldLogs()
    {
        const int retainedFileCount = 14;
        var oldFiles = Directory
            .EnumerateFiles(_paths.LogsRoot, "GitTool-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(retainedFileCount);

        foreach (var oldFile in oldFiles)
        {
            try
            {
                File.Delete(oldFile);
            }
            catch
            {
                // Rotation is best-effort and must not interrupt a Git operation.
            }
        }
    }
}
