using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using ShutdownClock.Models;
using ShutdownClock.Services;
using Windows.Foundation;
using Windows.UI;

namespace ShutdownClock.Views;

public sealed partial class FloatingClockWindow : Window
{
    /// <summary>圆环基准：正方形边长 150，环直径 = 150。</summary>
    private const double RingSize = 150;
    private const double RingThickness = 10;

    private System.Threading.Timer? _ticker;
    private CountdownStyle _style = CountdownStyle.BigDigits;
    private bool _isShrunk;
    private bool _closedByOwner;

    public ScheduledTask? Task { get; private set; }

    /// <summary>用户点"取消"时触发。</summary>
    public event EventHandler? CancelRequested;

    /// <summary>用户直接关闭窗口时触发（不是 CloseByOwner）。</summary>
    public event EventHandler? WindowClosed;

    public FloatingClockWindow()
    {
        InitializeComponent();

        Title = "ShutdownClock 倒计时";
        WindowHelper.SetSize(this, 420, 190);
        WindowHelper.MakeBorderless(this);
        WindowHelper.ApplyNoActivate(this);
        WindowHelper.MoveToCorner(this);

        ApplyTheme();
        ApplyStyle(_style);

        // 用户关窗口（点任务栏关闭等）
        Closed += (_, _) =>
        {
            _ticker?.Dispose();
            _ticker = null;
            if (!_closedByOwner) WindowClosed?.Invoke(this, EventArgs.Empty);
        };

        _ticker = new System.Threading.Timer(
            _ => RootGrid.DispatcherQueue.TryEnqueue(Refresh), null, 0, 200);
    }

    /// <summary>按系统深色 / 浅色模式套用配色。</summary>
    private void ApplyTheme()
    {
        Theme.Refresh();
        var dark = Theme.IsDark;

        RootBorder.Background = Solid(dark ? Color.FromArgb(0xE6, 0x10, 0x10, 0x14) : Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        RootBorder.BorderBrush = Solid(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0x00, 0x00, 0x00));

        TitleText.Foreground = Solid(dark ? Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xCC, 0x00, 0x00, 0x00));
        StatusText.Foreground = Solid(dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0x00, 0x00, 0x00));

        BigDigits.Foreground = Solid(Theme.Foreground);
        RingText.Foreground = Solid(Theme.Foreground);
        MinimalText.Foreground = Solid(Theme.SubtleForeground);

        RingTrackCircle.Stroke = Solid(Theme.RingTrack);
        RingArc.Stroke = Solid(Theme.RingProgress);
    }

    private static SolidColorBrush Solid(Color c) => new(c);

    public void AttachTask(ScheduledTask task)
    {
        Task = task;
        TitleText.Text = $"距{task.Action.ToDisplay()}";
        CancelButton.Visibility = Visibility.Visible;
        Refresh();
    }

    public void SetStyle(CountdownStyle style)
    {
        _style = style;
        ApplyStyle(style);
        Refresh();
    }

    private void ApplyStyle(CountdownStyle style)
    {
        BigDigits.Visibility = style == CountdownStyle.BigDigits ? Visibility.Visible : Visibility.Collapsed;
        RingHost.Visibility = style == CountdownStyle.ProgressRing ? Visibility.Visible : Visibility.Collapsed;
        MinimalText.Visibility = style == CountdownStyle.Minimal ? Visibility.Visible : Visibility.Collapsed;
        StyleButton.Content = style switch
        {
            CountdownStyle.BigDigits => "大字",
            CountdownStyle.ProgressRing => "进度环",
            _ => "极简"
        };
    }

    private void Refresh()
    {
        var task = Task;
        if (task is null) return;

        var left = task.Target - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;

        var text = Format(left);
        BigDigits.Text = text;
        RingText.Text = text;
        MinimalText.Text = $"{task.Action.ToDisplay()} 还有 {text}";

        var progress = task.Total.TotalSeconds > 0
            ? Math.Clamp(1 - left.TotalSeconds / task.Total.TotalSeconds, 0, 1)
            : 1;
        DrawRing(progress);

        StatusText.Text = $"{task.Target:yyyy-MM-dd HH:mm} {task.Action.ToDisplay()}";
    }

    /// <summary>
    /// 画进度弧。半径取 (边长 - 线宽)/2，让圆环外沿正好填满 150×150 的正方形。
    /// </summary>
    private void DrawRing(double progress)
    {
        var radius = (RingSize - RingThickness) / 2;
        var cx = RingSize / 2;
        var cy = RingSize / 2;

        RingTrackCircle.Width = radius * 2;
        RingTrackCircle.Height = radius * 2;

        if (progress <= 0)
        {
            RingArc.Data = null;
            return;
        }

        if (progress >= 1)
        {
            // 满环用整圆，避免起点终点重合画不出来
            RingArc.Data = new EllipseGeometry
            {
                Center = new Point(cx, cy),
                RadiusX = radius,
                RadiusY = radius
            };
            return;
        }

        var angle = 360 * progress;
        var rad = (angle - 90) * Math.PI / 180;
        var x = cx + radius * Math.Cos(rad);
        var y = cy + radius * Math.Sin(rad);

        var fig = new PathFigure
        {
            StartPoint = new Point(cx, cy - radius),
            IsClosed = false,
            IsFilled = false
        };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(x, y),
            Size = new Size(radius, radius),
            IsLargeArc = angle > 180,
            SweepDirection = SweepDirection.Clockwise
        });

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        RingArc.Data = geo;
    }

    public static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var days = (int)t.TotalDays;
        return days > 0
            ? $"{days}天 {t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    /// <summary>由主窗口主动关闭，不触发 WindowClosed。</summary>
    public void CloseByOwner()
    {
        _closedByOwner = true;
        _ticker?.Dispose();
        _ticker = null;
        try { Close(); } catch { }
    }

    // ---------- 交互 ----------

    private void OnBarEntered(object sender, PointerRoutedEventArgs e) => SetBarOpacity(1);

    private void OnBarExited(object sender, PointerRoutedEventArgs e) => SetBarOpacity(_isShrunk ? 0 : 1);

    private void SetBarOpacity(double v)
    {
        TopBar.Opacity = v;
        TopBar.IsHitTestVisible = v > 0.5;
    }

    private void OnCycleStyle(object sender, RoutedEventArgs e)
    {
        _style = _style switch
        {
            CountdownStyle.BigDigits => CountdownStyle.ProgressRing,
            CountdownStyle.ProgressRing => CountdownStyle.Minimal,
            _ => CountdownStyle.BigDigits
        };
        ApplyStyle(_style);
        Refresh();
    }

    private void OnToggleTopmost(object sender, RoutedEventArgs e)
    {
        var topmost = PinButton.Content as string == "置顶";
        WindowHelper.SetTopmost(this, !topmost);
        PinButton.Content = topmost ? "普通" : "置顶";
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnShrink(object sender, RoutedEventArgs e)
    {
        if (!_isShrunk)
        {
            _isShrunk = true;
            WindowHelper.SetSize(this, 220, 100);
            WindowHelper.MoveToCorner(this);
            SetBarOpacity(0);
            MinButton.Content = "放大";
        }
        else
        {
            _isShrunk = false;
            WindowHelper.SetSize(this, 420, 190);
            WindowHelper.MoveToCorner(this);
            SetBarOpacity(1);
            MinButton.Content = "－";
        }
    }
}
