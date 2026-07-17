using GitTool.App.Views;
using GitTool.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace GitTool.App;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1180;
    private const int InitialHeight = 760;
    private readonly AppWindow _appWindow;
    private bool _shutdownStarted;

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
        Closed += OnWindowClosed;
        App.Current.Services.OperationCoordinator.StatusChanged += OnOperationStatusChanged;

        MainNavigation.Loaded += (_, _) => SelectInitialPage();
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
        var x = workArea.X + Math.Max(0, (workArea.Width - InitialWidth) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - InitialHeight) / 2);

        _appWindow.Resize(new SizeInt32(InitialWidth, InitialHeight));
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

    private void ApplyOperationStatus(OperationSnapshot snapshot)
    {
        StatusTitleText.Text = snapshot.Title;
        StatusDetailText.Text = snapshot.Detail;
        BusyTitleText.Text = snapshot.Title;
        BusyDetailText.Text = snapshot.Detail;

        var isBusy = snapshot.IsBusy;
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        MainNavigation.IsEnabled = !isBusy;
        StatusProgressRing.IsActive = isBusy;
        StatusProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        StatusGlyph.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        StatusGlyph.Glyph = snapshot.State switch
        {
            OperationState.Completed => "\uE73E",
            OperationState.Failed => "\uEA39",
            _ => "\uE946"
        };
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.Current.Services.OperationCoordinator.Current.IsBusy)
        {
            args.Cancel = true;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        App.Current.Services.OperationCoordinator.StatusChanged -= OnOperationStatusChanged;
        App.Current.Services.ShutdownAsync().GetAwaiter().GetResult();
    }
}
