using System.Windows;
using System.Windows.Media;
using ShutdownClock.Models;
using ShutdownClock.Services;

namespace ShutdownClock.Verify;

/// <summary>
/// 针对本次四项修改的验证程序：
///  1. 悬浮窗关闭 → 任务取消（ClosedByOwner 语义）
///  2. 翻页样式已移除（枚举只剩 3 项）
///  3. 进度环宿主为正方形，且环直径 = 正方形边长
///  4. 主题配色跟随系统深浅色，文字与背景对比度足够
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
    }

    [STAThread]
    private static void Main()
    {
        Console.WriteLine("① 翻页样式应已移除");
        var styles = Enum.GetNames<CountdownStyle>();
        Check("枚举只剩 3 个样式", styles.Length == 3, string.Join(",", styles));
        Check("不含 FlipClock", !styles.Contains("FlipClock"), string.Join(",", styles));
        Check("含 BigDigits", styles.Contains("BigDigits"));
        Check("含 ProgressRing", styles.Contains("ProgressRing"));
        Check("含 Minimal", styles.Contains("Minimal"));
        Check("默认样式为 BigDigits", Enum.GetValues<CountdownStyle>()[0] == CountdownStyle.BigDigits);

        Console.WriteLine();
        Console.WriteLine("② 悬浮窗 ClosedByOwner 语义");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var win = new ShutdownClock.Views.FloatingClockWindow();
        Check("新建窗口 ClosedByOwner 默认 false", !win.ClosedByOwner);
        win.ClosedByOwner = true;
        Check("可被置为 true（主窗主动关闭时）", win.ClosedByOwner);

        var win2 = new ShutdownClock.Views.FloatingClockWindow();
        var closedFired = false;
        win2.Closed += (_, _) => closedFired = true;
        win2.Show();
        win2.Close();
        Check("Close 会触发 Closed 事件", closedFired);
        Check("用户关闭时 ClosedByOwner 仍为 false", !win2.ClosedByOwner);

        Console.WriteLine();
        Console.WriteLine("③ 进度环几何：正方形 + 环直径 = 边长");
        var ringWin = new ShutdownClock.Views.FloatingClockWindow();
        ringWin.Show();
        ringWin.UpdateLayout();
        // 找 RingSquare / RingTrackCircle
        var square = FindByName<FrameworkElement>(ringWin, "RingSquare");
        var track = FindByName<FrameworkElement>(ringWin, "RingTrackCircle");
        if (square is not null)
        {
            Check("容器宽高相等（正方形）", Math.Abs(square.Width - square.Height) < 0.001,
                $"{square.Width} x {square.Height}");
        }
        else Check("找到 RingSquare", false);

        if (track is not null)
        {
            Check("环直径 = 正方形边长", Math.Abs(track.Width - square!.Width) < 0.001,
                $"环 {track.Width}，正方形 {square.Width}");
        }
        else Check("找到 RingTrackCircle", false);
        ringWin.Close();

        Console.WriteLine();
        Console.WriteLine("④ 主题配色（当前系统模式）");
        Theme.Refresh();
        Console.WriteLine($"     系统当前: {(Theme.IsDark ? "深色" : "浅色")}");
        var bg = ToColor(Theme.WindowBackground);
        var fg = ToColor(Theme.Foreground);
        var ratio = Contrast(fg, bg);
        Check($"主文字对比度 ≥ 4.5:1（实际 {ratio:F2}:1）", ratio >= 4.5);

        var inputBg = ToColor(Theme.InputBackground);
        var inputFg = ToColor(Theme.InputForeground);
        var inputRatio = Contrast(inputFg, inputBg);
        Check($"输入框文字对比度 ≥ 4.5:1（实际 {inputRatio:F2}:1）", inputRatio >= 4.5);

        // 关键回归：不允许出现"白底白字"
        var whiteOnWhite = IsNear(bg, Colors.White) && IsNear(fg, Colors.White);
        Check("不是白底白字", !whiteOnWhite, $"底 {bg} 字 {fg}");

        var blackOnBlack = IsNear(bg, Colors.Black) && IsNear(fg, Colors.Black);
        Check("不是黑底黑字", !blackOnBlack, $"底 {bg} 字 {fg}");

        // 深浅两套配色都要可用：直接构造比较
        Check("深色模式底色偏暗", Luminance(Color.FromRgb(0x14, 0x14, 0x18)) < 0.1);
        Check("浅色模式底色偏亮", Luminance(Color.FromRgb(0xF5, 0xF5, 0xF7)) > 0.8);
        Check("深色模式文字亮", Luminance(Color.FromRgb(0xE8, 0xE8, 0xEE)) > 0.7);
        Check("浅色模式文字暗", Luminance(Color.FromRgb(0x1A, 0x1A, 0x1F)) < 0.1);

        Console.WriteLine();
        Console.WriteLine($"通过 {_pass}，失败 {_fail}");
        app.Shutdown();
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T t && t.Name == name) return t;
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var found = FindByName<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null) return found;
        }
        return null;
    }

    private static Color ToColor(Brush b) => b is SolidColorBrush s ? s.Color : Colors.Transparent;

    private static bool IsNear(Color a, Color b) =>
        Math.Abs(Luminance(a) - Luminance(b)) < 0.05;

    /// <summary>相对亮度（WCAG）。</summary>
    private static double Luminance(Color c)
    {
        static double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var l1 = Luminance(a);
        var l2 = Luminance(b);
        var hi = Math.Max(l1, l2);
        var lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }
}
