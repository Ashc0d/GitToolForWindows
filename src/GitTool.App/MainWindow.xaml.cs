using GitTool.App.Views;
using GitTool.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace GitTool.App;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1388;
    private const int InitialHeight = 1144;
    private const int ShowWindowRestore = 9;
    private readonly AppWindow _appWindow;
    private bool _shutdownStarted;
    private bool _cancellationDialogOpen;
    private bool _closeRequested;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureTitleBar();
        CenterWindow(windowId);

        _appWindow.Closing += OnAppWindowClosing;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
        App.Current.Services.OperationCoordinator.StatusChanged += OnOperationStatusChanged;

        MainNavigation.Loaded += (_, _) => SelectInitialPage();
    }

    internal void ActivateFromNotification()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(windowHandle, ShowWindowRestore);
        Activate();
        SetForegroundWindow(windowHandle);
    }

    private void ConfigureTitleBar()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void CenterWindow(WindowId windowId)
    {
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var width = Math.Min(InitialWidth, workArea.Width);
        var height = Math.Min(InitialHeight, workArea.Height);
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

        _appWindow.Resize(new SizeInt32(width, height));
        _appWindow.Move(new PointInt32(x, y));
    }

    private void SelectInitialPage()
    {
        if (MainNavigation.MenuItems.FirstOrDefault() is NavigationViewItem firstItem)
        {
            MainNavigation.SelectedItem = firstItem;
            Navigate("clone");
        }
    }

    private void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "clone" => typeof(ClonePage),
            "repository" => typeof(RepositoryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(ClonePage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void OnOperationStatusChanged(object? sender, OperationSnapshot snapshot)
    {
        DispatcherQueue.TryEnqueue(() => ApplyOperationStatus(snapshot));
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args) =>
        App.Current.Services.NotificationService.SetForegroundState(
            args.WindowActivationState != WindowActivationState.Deactivated);

    private void ApplyOperationStatus(OperationSnapshot snapshot)
    {
        StatusTitleText.Text = snapshot.Title;
        StatusDetailText.Text = snapshot.Detail;
        BusyTitleText.Text = snapshot.Title;
        BusyDetailText.Text = snapshot.Detail;

        var isBusy = snapshot.IsBusy;
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        MainNavigation.IsEnabled = !isBusy;
        BusyCancelButton.IsEnabled = snapshot.State == OperationState.Running;
        StatusProgressRing.IsActive = isBusy;
        StatusProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        StatusGlyph.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        StatusGlyph.Glyph = snapshot.State switch
        {
            OperationState.Completed => "\uE73E",
            OperationState.Cancelled => "\uE711",
            OperationState.Failed => "\uEA39",
            _ => "\uE946"
        };
    }

    private async void OnCancelOperationClick(object sender, RoutedEventArgs args)
    {
        await ConfirmCancellationAsync(closeWhenFinished: false);
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _shutdownStarted)
        {
            return;
        }

        var snapshot = App.Current.Services.OperationCoordinator.Current;
        if (!snapshot.IsBusy)
        {
            // Let the original system close request complete naturally. Final
            // service cleanup runs once from Window.Closed.
            return;
        }

        // Active operations require an asynchronous confirmation/cancellation
        // sequence, so only this close request is deferred.
        args.Cancel = true;

        if (snapshot.State == OperationState.Cancelling)
        {
            DispatcherQueue.TryEnqueue(RequestCloseAfterCurrentOperation);
            return;
        }

        DispatcherQueue.TryEnqueue(() => _ = ConfirmCancellationAsync(closeWhenFinished: true));
    }

    private async Task ConfirmCancellationAsync(bool closeWhenFinished)
    {
        if (_cancellationDialogOpen)
        {
            return;
        }

        var coordinator = App.Current.Services.OperationCoordinator;
        if (!coordinator.Current.IsBusy)
        {
            if (closeWhenFinished)
            {
                RequestCloseAfterCurrentOperation();
            }

            return;
        }

        _cancellationDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Cancel current Git operation?",
                Content = new TextBlock
                {
                    Text = closeWhenFinished
                        ? "GitTool will stop the active Git process, finish safe clone cleanup, and then close."
                        : "GitTool will stop the active Git process and finish safe clone cleanup before unlocking the app.",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480
                },
                PrimaryButtonText = closeWhenFinished ? "Cancel and close" : "Cancel operation",
                CloseButtonText = "Keep running",
                DefaultButton = ContentDialogButton.Close
            };

            var decision = await dialog.ShowAsync();
            if (decision != ContentDialogResult.Primary)
            {
                return;
            }

            BusyCancelButton.IsEnabled = false;
            coordinator.CancelCurrentOperation();

            if (closeWhenFinished)
            {
                RequestCloseAfterCurrentOperation();
            }
        }
        finally
        {
            _cancellationDialogOpen = false;
        }
    }

    private void RequestCloseAfterCurrentOperation()
    {
        if (_closeRequested || _shutdownStarted)
        {
            return;
        }

        _closeRequested = true;
        App.Current.Services.NotificationService.SuppressForShutdown();
        try
        {
            _appWindow.Hide();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"GitTool could not hide the window during deferred close: {exception}");
        }

        _ = CloseAfterCurrentOperationAsync();
    }

    private async Task CloseAfterCurrentOperationAsync()
    {
        try
        {
            await App.Current.Services.UserOperations.WaitForCurrentOperationUiCompletionAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"GitTool operation UI settlement failed during close: {exception}");
        }

        _allowClose = true;
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _closeRequested = false;
        Activated -= OnWindowActivated;
        App.Current.Services.OperationCoordinator.StatusChanged -= OnOperationStatusChanged;

        try
        {
            var shutdownTask = App.Current.Services.ShutdownAsync();
            if (!shutdownTask.Wait(TimeSpan.FromSeconds(1)))
            {
                System.Diagnostics.Debug.WriteLine(
                    "GitTool shutdown cleanup exceeded one second; exiting without waiting longer.");
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"GitTool shutdown cleanup failed: {exception}");
        }
        finally
        {
            App.Current.Exit();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
