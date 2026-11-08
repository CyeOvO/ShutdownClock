using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ShutdownClock.Models;

namespace ShutdownClock.Views;

public partial class FloatingClockWindow : Window
{
    /// <summary>圆环几何基准尺寸：正方形边长 150，环直径 = 150。</summary>
    private const double RingSize = 150;
    private const double RingThickness = 10;

    private System.Threading.Timer? _ticker;
    private CountdownStyle _style = CountdownStyle.BigDigits;
    private bool _isShrunk;
    private Rect _restoreBounds;

    public ScheduledTask? Task { get; private set; }

    /// <summary>由主窗口主动关闭（取消 / 完成）时置 true，用来区分"用户点了关闭"。</summary>
    public bool ClosedByOwner { get; set; }

    /// <summary>用户点"取消"时触发。</summary>
    public event EventHandler? CancelRequested;

    /// <summary>每帧刷新时触发，用于主窗口同步状态。</summary>
    public event EventHandler<TimeSpan>? Tick;

    public FloatingClockWindow()
    {
        InitializeComponent();

        _restoreBounds = new Rect(Left, Top, Width, Height);

        ApplyTheme();
        Loaded += OnLoaded;
        Closed += OnClosed;
        MouseEnter += (_, _) => SetBarOpacity(1);
        MouseLeave += (_, _) => SetBarOpacity(_isShrunk ? 0 : 1);

        ApplyStyle(_style);
        MoveToCorner();
    }

    /// <summary>按系统深色 / 浅色模式套用配色，避免深底深字或白底白字。</summary>
    private void ApplyTheme()
    {
        var dark = Services.Theme.IsDark;

        RootBorder.Background = dark ? Brush("#E6101014") : Brush("#F2FFFFFF");
        RootBorder.BorderBrush = dark ? Brush("#33FFFFFF") : Brush("#22000000");

        TitleText.Foreground = dark ? Brush("#CCFFFFFF") : Brush("#CC000000");
        StatusText.Foreground = dark ? Brush("#99FFFFFF") : Brush("#99000000");

        BigDigits.Foreground = dark ? Brushes.White : Brush("#FF1A1A1F");
        MinimalText.Foreground = dark ? Brush("#FFB4B4C0") : Brush("#FF5A5A66");
        RingText.Foreground = dark ? Brushes.White : Brush("#FF1A1A1F");

        RingTrackCircle.Stroke = dark ? Brush("#33FFFFFF") : Brush("#33000000");
        RingArc.Stroke = dark ? Brush("#FF5AC8FA") : Brush("#FF1A73E8");

        if (dark)
        {
            foreach (var b in new[] { StyleButton, PinButton, CancelButton, MinButton })
                b.Foreground = Brushes.White;
        }
        else
        {
            foreach (var b in new[] { StyleButton, PinButton, CancelButton, MinButton })
                b.Foreground = Brush("#FF1A1A1F");
        }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 置顶但"不抢焦点"：避免打断用户当前工作
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        var ex = Native.GetWindowLong(helper.Handle, Native.GWL_EXSTYLE);
        Native.SetWindowLong(helper.Handle, Native.GWL_EXSTYLE,
            ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);

        _ticker?.Dispose();
        _ticker = new System.Threading.Timer(_ => Dispatcher.Invoke(Refresh), null, 0, 200);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ticker?.Dispose();
        _ticker = null;
    }

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

        Tick?.Invoke(this, left);
    }

    /// <summary>
    /// 画进度弧。半径取 (正方形边长 - 线宽)/2，使圆环外沿正好填满 150×150 的正方形，
    /// 即"背景是与环直径相等的正方形"。
    /// </summary>
    private void DrawRing(double progress)
    {
        var radius = (RingSize - RingThickness) / 2;
        var cx = RingSize / 2;
        var cy = RingSize / 2;

        // 底环同样按这个半径对齐
        RingTrackCircle.Width = radius * 2;
        RingTrackCircle.Height = radius * 2;

        if (progress <= 0)
        {
            RingArc.Data = Geometry.Empty;
            return;
        }

        if (progress >= 1)
        {
            // 满环直接用整圆，避免起点终点重合时 ArcSegment 画不出东西
            RingArc.Data = new EllipseGeometry(new Point(cx, cy), radius, radius);
            return;
        }

        var angle = 360 * progress;
        var rad = (angle - 90) * Math.PI / 180;
        var x = cx + radius * Math.Cos(rad);
        var y = cy + radius * Math.Sin(rad);

        var fig = new PathFigure { StartPoint = new Point(cx, cy - radius), IsClosed = false };
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

    // ---------- 交互 ----------

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (_isShrunk)
        {
            try { DragMove(); } catch { }
            SavePosition();
        }
    }

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
        Topmost = !Topmost;
        PinButton.Content = Topmost ? "置顶" : "普通";
        PinButton.Background = Topmost
            ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80));
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnShrink(object sender, RoutedEventArgs e)
    {
        if (!_isShrunk)
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            _isShrunk = true;
            Width = 220;
            Height = 100;
            MoveToCorner();
            SetBarOpacity(0);
            MinButton.Content = "放大";
        }
        else
        {
            _isShrunk = false;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            SetBarOpacity(1);
            MinButton.Content = "－";
        }
    }

    /// <summary>贴到屏幕右下角，不挡工作区。</summary>
    public void MoveToCorner()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 16;
        Top = wa.Bottom - Height - 16;
    }

    public void SavePosition() => _restoreBounds = new Rect(Left, Top, Width, Height);

    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
