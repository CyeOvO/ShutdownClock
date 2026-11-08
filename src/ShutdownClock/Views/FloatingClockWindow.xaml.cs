using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ShutdownClock.Controls;
using ShutdownClock.Models;

namespace ShutdownClock.Views;

public partial class FloatingClockWindow : Window
{
    private readonly FlipClock _flip = new();
    private System.Threading.Timer? _ticker;
    private CountdownStyle _style = CountdownStyle.FlipClock;
    private bool _isShrunk;
    private Rect _restoreBounds;

    public ScheduledTask? Task { get; private set; }

    /// <summary>用户点"取消"时触发。</summary>
    public event EventHandler? CancelRequested;

    /// <summary>每帧刷新时触发，用于主窗口同步状态。</summary>
    public event EventHandler<TimeSpan>? Tick;

    public FloatingClockWindow()
    {
        InitializeComponent();
        FlipHost.Content = _flip;

        // 记住原有位置，缩小后能还原
        _restoreBounds = new Rect(Left, Top, Width, Height);

        Loaded += OnLoaded;
        Closed += OnClosed;
        MouseEnter += (_, _) => SetBarOpacity(1);
        MouseLeave += (_, _) => SetBarOpacity(_isShrunk ? 0 : 1);

        ApplyStyle(_style);
        MoveToCorner();
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
        FlipHost.Visibility = style == CountdownStyle.FlipClock ? Visibility.Visible : Visibility.Collapsed;
        BigDigits.Visibility = style == CountdownStyle.BigDigits ? Visibility.Visible : Visibility.Collapsed;
        RingHost.Visibility = style == CountdownStyle.ProgressRing ? Visibility.Visible : Visibility.Collapsed;
        MinimalText.Visibility = style == CountdownStyle.Minimal ? Visibility.Visible : Visibility.Collapsed;
        StyleButton.Content = style switch
        {
            CountdownStyle.FlipClock => "翻页",
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

        _flip.Update(left);

        var text = Format(left);
        BigDigits.Text = text;
        RingText.Text = text;
        MinimalText.Text = $"{task.Action.ToDisplay()} 还有 {text}";

        // 进度环
        var progress = task.Total.TotalSeconds > 0
            ? Math.Clamp(1 - left.TotalSeconds / task.Total.TotalSeconds, 0, 1)
            : 1;
        DrawRing(progress);

        StatusText.Text = $"{task.Target:yyyy-MM-dd HH:mm} {task.Action.ToDisplay()}";

        Tick?.Invoke(this, left);
    }

    private void DrawRing(double progress)
    {
        const double size = 130, radius = size / 2, cx = 10 + radius, cy = 10 + radius;
        if (progress <= 0)
        {
            RingArc.Data = Geometry.Empty;
            return;
        }
        var angle = 360 * progress;
        var rad = (angle - 90) * Math.PI / 180;
        var x = cx + radius * Math.Cos(rad);
        var y = cy + radius * Math.Sin(rad);
        var isLarge = angle > 180 ? 1 : 0;

        var fig = new PathFigure { StartPoint = new Point(cx, cy - radius), IsClosed = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(x, y),
            Size = new Size(radius, radius),
            IsLargeArc = isLarge == 1,
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
            // 缩小状态下拖动整窗
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
            CountdownStyle.FlipClock => CountdownStyle.BigDigits,
            CountdownStyle.BigDigits => CountdownStyle.ProgressRing,
            CountdownStyle.ProgressRing => CountdownStyle.Minimal,
            _ => CountdownStyle.FlipClock
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
