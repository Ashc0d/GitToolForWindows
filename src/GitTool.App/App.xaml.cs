using GitTool.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GitTool.App;

public partial class App : Application
{
    private readonly DispatcherQueue _dispatcherQueue;
    private MainWindow? _mainWindow;
    private int _pendingNotificationActivation;

    public App()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public new static App Current => (App)Application.Current;

    public AppServices Services { get; private set; } = null!;

    public nint MainWindowHandle { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = new AppServices();
        await Services.InitializeAsync(OnNotificationInvoked);

        _mainWindow = new MainWindow();
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
        if (Interlocked.Exchange(ref _pendingNotificationActivation, 0) == 1)
        {
            _mainWindow.ActivateFromNotification();
        }
        else
        {
            _mainWindow.Activate();
        }
    }

    private void OnNotificationInvoked(string arguments)
    {
        Interlocked.Exchange(ref _pendingNotificationActivation, 1);
        _dispatcherQueue.TryEnqueue(ActivateMainWindowFromNotification);
    }

    private void ActivateMainWindowFromNotification()
    {
        if (_mainWindow is null)
        {
            return;
        }

        Interlocked.Exchange(ref _pendingNotificationActivation, 0);
        _mainWindow.ActivateFromNotification();
    }
}
