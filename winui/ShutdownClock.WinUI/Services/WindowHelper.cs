using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace ShutdownClock.Services;

/// <summary>WinUI 3 窗口的尺寸、主题、材质等辅助操作。</summary>
public static class WindowHelper
{
    /// <summary>设置窗口客户区尺寸（逻辑像素，按当前 DPI 换算）。</summary>
    public static void SetSize(Window window, int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);

        var scale = GetDpiScale(hwnd);
        appWindow.Resize(new SizeInt32(
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale)));
    }

    /// <summary>把窗口在屏幕居中。</summary>
    public static void CenterOnScreen(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);

        var screen = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary).WorkArea;
        var size = appWindow.Size;

        appWindow.Move(new PointInt32(
            screen.X + (screen.Width - size.Width) / 2,
            screen.Y + (screen.Height - size.Height) / 2));
    }

    /// <summary>尝试套用 Mica 材质；系统不支持时静默跳过。</summary>
    public static bool TryApplyMica(Window window)
    {
        try
        {
            if (!MicaController.IsSupported()) return false;
            window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>当前窗口是否用上了 Mica（用上了就不用再画纯色背景）。</summary>
    public static bool IsMicaActive(Window window)
        => window.SystemBackdrop is MicaBackdrop;

    /// <summary>
    /// 窗口扩展样式：不抢焦点 + 不出现在 Alt+Tab。
    /// 悬浮倒计时窗用这个避免打断用户正在进行的输入。
    /// </summary>
    public static void ApplyNoActivate(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>让窗口置顶 / 取消置顶。</summary>
    public static void SetTopmost(Window window, bool topmost)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var insertAfter = topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
        SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>去掉标题栏，用于无边框悬浮窗。</summary>
    public static void MakeBorderless(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    /// <summary>窗口尺寸变化事件。</summary>
    public static void OnSizeChanged(Window window, Action<int, int> handler)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Changed += (_, args) =>
        {
            if (!args.DidSizeChange) return;
            handler(appWindow.Size.Width, appWindow.Size.Height);
        };
    }

    /// <summary>移到主屏工作区右下角。</summary>
    public static void MoveToCorner(Window window, int marginRight = 16, int marginBottom = 16)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);

        var work = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary).WorkArea;
        var size = appWindow.Size;

        appWindow.Move(new PointInt32(
            work.X + work.Width - size.Width - marginRight,
            work.Y + work.Height - size.Height - marginBottom));
    }

    public static void MoveTo(Window window, int x, int y)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(id).Move(new PointInt32(x, y));
    }

    public static (int X, int Y, int W, int H) GetBounds(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        return (appWindow.Position.X, appWindow.Position.Y, appWindow.Size.Width, appWindow.Size.Height);
    }

    public static double GetDpiScale(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi <= 0 ? 1.0 : dpi / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    // ---------- P/Invoke ----------

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
