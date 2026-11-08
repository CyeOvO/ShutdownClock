using System.Windows;
using ShutdownClock.Views;

namespace ShutdownClock;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 全局异常兜底，避免直接闪退
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "ShutdownClock 出错",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }
}
