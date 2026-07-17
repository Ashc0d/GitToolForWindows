using GitTool.Core.Infrastructure;
using GitTool.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitTool.App.Services;

public sealed class UserOperationService
{
    private readonly OperationCoordinator _coordinator;
    private readonly TaskbarBadgeService _badgeService;
    private readonly AppNotificationService _notificationService;
    private readonly IAppLogger _logger;

    public UserOperationService(
        OperationCoordinator coordinator,
        TaskbarBadgeService badgeService,
        AppNotificationService notificationService,
        IAppLogger logger)
    {
        _coordinator = coordinator;
        _badgeService = badgeService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<OperationResult> RunAsync(
        string title,
        XamlRoot xamlRoot,
        Func<IProgress<string>, CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken = default)
    {
        _badgeService.ShowActivity();
        var result = await _coordinator.ExecuteAsync(title, operation, cancellationToken);

        if (result.IsSuccess)
        {
            _badgeService.Clear();
            _logger.Info(result.Summary);
            return result;
        }

        _badgeService.ShowError();
        await _logger.ErrorAsync($"{title}: {result.Summary}{Environment.NewLine}{result.Diagnostics}");
        _notificationService.ShowAttention("GitTool needs attention", result.Summary);
        await ShowErrorDialogAsync(xamlRoot, title, result);
        _badgeService.Clear();
        return result;
    }

    private static async Task ShowErrorDialogAsync(
        XamlRoot xamlRoot,
        string title,
        OperationResult result)
    {
        var diagnosticText = result.Diagnostics;
        if (diagnosticText.Length > 12_000)
        {
            diagnosticText = diagnosticText[^12_000..];
        }

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = result.Summary,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBox
        {
            Text = diagnosticText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 520,
            MaxHeight = 300
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"{title} stopped",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }
}
