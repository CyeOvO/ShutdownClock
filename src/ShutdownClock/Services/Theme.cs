using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace ShutdownClock.Services;

/// <summary>
/// 跟随 Windows「个性化 → 颜色 → 选择默认应用模式」的主题配色。
/// 深色模式下用深底白字，浅色模式下用白底深字，避免白底白字看不清。
/// </summary>
public static class Theme
{
    /// <summary>系统当前是否为深色模式。</summary>
    public static bool IsDark { get; private set; } = true;

    // ---------- 主窗口 ----------

    /// <summary>窗口背景。</summary>
    public static Brush WindowBackground { get; private set; } = Brushes.Black;

    /// <summary>卡片 / 分区背景。</summary>
    public static Brush CardBackground { get; private set; } = Brushes.Black;

    /// <summary>主文字颜色。</summary>
    public static Brush Foreground { get; private set; } = Brushes.White;

    /// <summary>次要文字颜色（标题说明、提示）。</summary>
    public static Brush SubtleForeground { get; private set; } = Brushes.Gray;

    /// <summary>更弱的提示文字。</summary>
    public static Brush FaintForeground { get; private set; } = Brushes.Gray;

    /// <summary>输入框背景。</summary>
    public static Brush InputBackground { get; private set; } = Brushes.Black;

    /// <summary>输入框边框。</summary>
    public static Brush InputBorder { get; private set; } = Brushes.Gray;

    /// <summary>输入框文字。</summary>
    public static Brush InputForeground { get; private set; } = Brushes.White;

    /// <summary>普通按钮背景。</summary>
    public static Brush ButtonBackground { get; private set; } = Brushes.Gray;

    /// <summary>普通按钮文字。</summary>
    public static Brush ButtonForeground { get; private set; } = Brushes.White;

    /// <summary>进度环的底环 / 未完成部分。</summary>
    public static Brush RingTrack { get; private set; } = Brushes.Gray;

    /// <summary>进度环的进度色。</summary>
    public static Brush RingProgress { get; private set; } = Brushes.DeepSkyBlue;

    /// <summary>代码 / 倒计时数字字体。</summary>
    public static FontFamily MonoFont { get; } = new("Consolas, Cascadia Mono, Segoe UI, Microsoft YaHei");

    /// <summary>界面字体。</summary>
    public static FontFamily UiFont { get; } = new("Microsoft YaHei UI, Segoe UI");

    static Theme()
    {
        Refresh();
    }

    /// <summary>重新读取系统主题并更新所有画刷。</summary>
    public static void Refresh()
    {
        IsDark = ReadIsDark();

        if (IsDark)
        {
            WindowBackground = Frozen(0x14, 0x14, 0x18);
            CardBackground = Frozen(0x1A, 0x1A, 0x20);
            Foreground = Frozen(0xE8, 0xE8, 0xEE);
            SubtleForeground = Argb(0x99, 0xFF, 0xFF, 0xFF);
            FaintForeground = Argb(0x77, 0xFF, 0xFF, 0xFF);
            InputBackground = Frozen(0x1E, 0x1E, 0x24);
            InputBorder = Argb(0x33, 0xFF, 0xFF, 0xFF);
            InputForeground = Frozen(0xFF, 0xFF, 0xFF);
            ButtonBackground = Frozen(0x25, 0x25, 0x30);
            ButtonForeground = Frozen(0xE8, 0xE8, 0xEE);
            RingTrack = Argb(0x33, 0xFF, 0xFF, 0xFF);
            RingProgress = Frozen(0x5A, 0xC8, 0xFA);
        }
        else
        {
            WindowBackground = Frozen(0xF5, 0xF5, 0xF7);
            CardBackground = Frozen(0xFF, 0xFF, 0xFF);
            Foreground = Frozen(0x1A, 0x1A, 0x1F);
            SubtleForeground = Argb(0xB3, 0x00, 0x00, 0x00);
            FaintForeground = Argb(0x8C, 0x00, 0x00, 0x00);
            InputBackground = Frozen(0xFF, 0xFF, 0xFF);
            InputBorder = Argb(0x33, 0x00, 0x00, 0x00);
            InputForeground = Frozen(0x1A, 0x1A, 0x1F);
            ButtonBackground = Frozen(0xEC, 0xEC, 0xF0);
            ButtonForeground = Frozen(0x1A, 0x1A, 0x1F);
            RingTrack = Argb(0x33, 0x00, 0x00, 0x00);
            RingProgress = Frozen(0x1A, 0x73, 0xE8);
        }
    }

    private static bool ReadIsDark()
    {
        try
        {
            // Windows 10 1809+ / Windows 11
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0;
        }
        catch
        {
            // 读不到就按深色处理，与旧版行为一致
        }
        return true;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Argb(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
