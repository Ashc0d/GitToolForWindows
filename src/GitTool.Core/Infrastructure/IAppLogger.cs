namespace GitTool.Core.Infrastructure;

public interface IAppLogger
{
    void Info(string message);

    void Warning(string message);

    Task ErrorAsync(string message, Exception? exception = null);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
