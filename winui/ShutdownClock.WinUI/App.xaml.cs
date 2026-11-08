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

        // 主窗口关掉就等于退出程序：WinUI 3 不会自动退出，
        // 不显式 Exit 的话悬浮窗就算关了，进程也可能挂在后台。
        _window.Closed += (_, _) => Exit();

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
