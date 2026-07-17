using GitTool.App.Services;
using Microsoft.UI.Xaml;

namespace GitTool.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    public new static App Current => (App)Application.Current;

    public AppServices Services { get; private set; } = null!;

    public nint MainWindowHandle { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = new AppServices();
        await Services.InitializeAsync();

        _mainWindow = new MainWindow();
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
        _mainWindow.Activate();
    }
}
