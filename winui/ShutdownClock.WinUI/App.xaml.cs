using Microsoft.UI.Xaml;
using ShutdownClock.Views;

namespace ShutdownClock;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 记录异常，避免界面无提示地直接退出
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "shutdownclock-error.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e.Exception}{Environment.NewLine}");
        }
        catch { }
    }
}
