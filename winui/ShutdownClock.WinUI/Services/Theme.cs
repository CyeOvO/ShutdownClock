using Microsoft.Win32;
using Microsoft.UI;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ShutdownClock.Services;

/// <summary>
/// 跟随系统深浅色的配色。WinUI 3 本身支持主题资源，
/// 但悬浮窗需要用到具体颜色值（自绘圆环、动态画刷），这里统一提供。
/// </summary>
public static class Theme
{
    /// <summary>系统当前是否为深色模式。</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>窗口背景。</summary>
    public static Color WindowBackground { get; private set; }

    /// <summary>卡片背景。</summary>
    public static Color CardBackground { get; private set; }

    /// <summary>主文字。</summary>
    public static Color Foreground { get; private set; }

    /// <summary>次要文字。</summary>
    public static Color SubtleForeground { get; private set; }

    /// <summary>更弱的提示文字。</summary>
    public static Color FaintForeground { get; private set; }

    /// <summary>进度环底环。</summary>
    public static Color RingTrack { get; private set; }

    /// <summary>进度环进度色。</summary>
    public static Color RingProgress { get; private set; }

    static Theme()
    {
        Refresh();
    }

    /// <summary>读取系统主题并刷新配色。</summary>
    public static void Refresh()
    {
        IsDark = ReadIsDark();

        if (IsDark)
        {
            WindowBackground = Rgb(0x14, 0x14, 0x18);
            CardBackground = Rgb(0x1A, 0x1A, 0x20);
            Foreground = Rgb(0xE8, 0xE8, 0xEE);
            SubtleForeground = Argb(0xCC, 0xFF, 0xFF, 0xFF);
            FaintForeground = Argb(0x99, 0xFF, 0xFF, 0xFF);
            RingTrack = Argb(0x33, 0xFF, 0xFF, 0xFF);
            RingProgress = Rgb(0x5A, 0xC8, 0xFA);
        }
        else
        {
            WindowBackground = Rgb(0xF5, 0xF5, 0xF7);
            CardBackground = Rgb(0xFF, 0xFF, 0xFF);
            Foreground = Rgb(0x1A, 0x1A, 0x1F);
            SubtleForeground = Argb(0xCC, 0x00, 0x00, 0x00);
            FaintForeground = Argb(0x99, 0x00, 0x00, 0x00);
            RingTrack = Argb(0x33, 0x00, 0x00, 0x00);
            RingProgress = Rgb(0x1A, 0x73, 0xE8);
        }
    }

    /// <summary>把系统主题应用到指定窗口（配合 <c>WindowThemeSync</c> 监听切换）。</summary>
    public static void ApplyTo(Microsoft.UI.Xaml.FrameworkElement root)
    {
        Refresh();
        if (root is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            panel.RequestedTheme = IsDark
                ? Microsoft.UI.Xaml.ElementTheme.Dark
                : Microsoft.UI.Xaml.ElementTheme.Light;
        }
    }

    private static bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch
        {
            // 读不到就按深色处理
        }
        return true;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    /// <summary>十六进制字符串转 Color，便于和 XAML 里的写法对照。</summary>
    public static Color FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 8)
        {
            return Color.FromArgb(
                Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16),
                Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
        }
        return Color.FromArgb(0xFF,
            Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16),
            Convert.ToByte(s[4..6], 16));
    }
}
