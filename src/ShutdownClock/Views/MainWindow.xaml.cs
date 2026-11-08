using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShutdownClock.Models;
using ShutdownClock.Services;

namespace ShutdownClock.Views;

public partial class MainWindow : Window
{
    private FloatingClockWindow? _clock;
    private ScheduledTask? _task;
    private System.Threading.Timer? _ticker;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme();
        TimeBox.Text = DateTime.Now.AddMinutes(30).ToString("yyyy-MM-dd HH:mm");
        // 清单已声明 requireAdministrator，正常情况总是已提权；保守起见仍做检测
        if (!ElevationService.IsAdministrator()) ElevateBar.Visibility = Visibility.Visible;
        UpdateState();
    }

    /// <summary>
    /// 按系统深色 / 浅色模式套用配色。
    /// 之前 XAML 里写死了深色（#141418 底 + 白字），在系统浅色模式下会出现
    /// 白底白字看不见的问题，这里统一改为跟随主题。
    /// </summary>
    private void ApplyTheme()
    {
        Theme.Refresh();
        var dark = Theme.IsDark;

        Background = Theme.WindowBackground;
        Foreground = Theme.Foreground;

        // 标题
        AppTitle.Foreground = Theme.Foreground;
        AppSubtitle.Foreground = Theme.SubtleForeground;

        // 四张卡片
        foreach (var card in new[] { ActionCard, TimeCard, StyleCard, StateCard })
            card.Background = Theme.CardBackground;

        // 分区小标题
        foreach (var label in new[] { ActionLabel, TimeLabel, StyleLabel, StateLabel })
            label.Foreground = Theme.SubtleForeground;

        // 说明文字
        FormatHint.Foreground = Theme.FaintForeground;
        QuickLabel.Foreground = Theme.FaintForeground;
        HintText.Foreground = Theme.SubtleForeground;

        // 动作单选
        foreach (var rb in new[] { RbShutdown, RbRestart, RbHibernate, RbSleep })
            rb.Foreground = Theme.Foreground;

        // 输入框
        TimeBox.Background = Theme.InputBackground;
        TimeBox.Foreground = Theme.InputForeground;
        TimeBox.BorderBrush = Theme.InputBorder;
        TimeBox.CaretBrush = Theme.InputForeground;

        // 快捷按钮
        foreach (var b in FindVisualChildren<Button>(this))
        {
            if (b.Style == (Style)FindResource("IconQuick"))
            {
                b.Background = Theme.ButtonBackground;
                b.Foreground = Theme.ButtonForeground;
            }
        }

        // 状态文字
        StateText.Foreground = Theme.FaintForeground;

        // 下拉框（WPF 默认模板在深色模式下会白底白字，这里显式设置）
        StyleBox.Background = Theme.InputBackground;
        StyleBox.Foreground = Theme.InputForeground;
        StyleBox.BorderBrush = Theme.InputBorder;

        // 「恢复已排定的系统关机」是次要动作，跟着主题走
        AbortButton.Background = dark ? Brush("#FF3A3A44") : Brush("#FFE0E0E6");
        AbortButton.Foreground = dark ? Brushes.White : Brush("#FF1A1A1F");
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }

    /// <summary>系统在窗口显示后切换主题时，微软会广播这条消息，这里同步刷新。</summary>
    private const int WM_SETTINGCHANGE = 0x001A;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero)
        {
            var area = Marshal.PtrToStringAuto(lParam);
            if (string.Equals(area, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTheme();
            }
        }
        return IntPtr.Zero;
    }

    private PowerAction SelectedAction =>
        RbRestart.IsChecked == true ? PowerAction.Restart :
        RbHibernate.IsChecked == true ? PowerAction.Hibernate :
        RbSleep.IsChecked == true ? PowerAction.Sleep :
        PowerAction.Shutdown;

    private CountdownStyle SelectedStyle => StyleBox.SelectedIndex switch
    {
        1 => CountdownStyle.ProgressRing,
        2 => CountdownStyle.Minimal,
        _ => CountdownStyle.BigDigits
    };

    // ---------- 快捷按钮 ----------

    private void OnElevate(object sender, RoutedEventArgs e)
    {
        // 先把已排定的系统关机撤掉，避免重复
        if (_task is not null && _task.Action.UsesSystemTimer())
        {
            PowerService.AbortSystemShutdown(out _);
            _task = null;
            _clock?.Close();
            _clock = null;
        }

        if (ElevationService.RestartAsAdministrator())
        {
            Application.Current.Shutdown();
        }
        else
        {
            HintText.Text = "已取消提权（可能点了 UAC 的否）。";
        }
    }

    private void OnQuick5(object sender, RoutedEventArgs e) => SetQuick(5);
    private void OnQuick15(object sender, RoutedEventArgs e) => SetQuick(15);
    private void OnQuick30(object sender, RoutedEventArgs e) => SetQuick(30);
    private void OnQuick60(object sender, RoutedEventArgs e) => SetQuick(60);
    private void OnQuick120(object sender, RoutedEventArgs e) => SetQuick(120);

    private void SetQuick(int minutes)
    {
        TimeBox.Text = DateTime.Now.AddMinutes(minutes).ToString("yyyy-MM-dd HH:mm");
        HintText.Text = $"已填入 {DateTime.Now.AddMinutes(minutes):HH:mm}";
    }

    private void OnTimeBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnStartClick(sender, e);
    }

    // ---------- 开始 ----------

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        var action = SelectedAction;
        var parsed = TimeInputParser.Parse(TimeBox.Text, DateTime.Now);

        if (!parsed.Ok)
        {
            HintText.Text = parsed.Message ?? "时间无效";
            return;
        }

        var target = parsed.Time!.Value;
        if ((target - DateTime.Now).TotalSeconds > 315360000)
        {
            HintText.Text = "时间太远，最多 10 年以内。";
            return;
        }

        HintText.Text = string.Empty;

        _task = new ScheduledTask { Target = target, Action = action };

        // 关机 / 重启：交给系统计时，关掉本程序也生效
        if (action.UsesSystemTimer())
        {
            if (!PowerService.ScheduleSystem(target, action, out var err))
            {
                HintText.Text = "系统排定失败：" + err;
                _task = null;
                return;
            }
        }

        ShowClock(_task);
        StartTicker();
        UpdateState();
    }

    private void ShowClock(ScheduledTask task)
    {
        if (_clock is null || !_clock.IsLoaded)
        {
            _clock = new FloatingClockWindow();
            _clock.CancelRequested += (_, _) => CancelTask();
            _clock.Closed += (_, _) =>
            {
                var closing = _clock;
                _clock = null;

                // 用户直接关掉悬浮窗 = 取消本次排定，主窗倒计时同步停止。
                // Finish / CancelTask 主动关闭窗口的情况已在 _clock 置空前处理掉，不会走到这里重复取消。
                if (closing is not null && !closing.ClosedByOwner
                    && _task is not null && !_task.Cancelled)
                {
                    CancelTask("已关闭倒计时窗口，定时任务已取消。");
                }
            };
        }

        if (!_clock.IsVisible) _clock.Show();
        _clock.SetStyle(SelectedStyle);
        _clock.AttachTask(task);
    }

    private void StartTicker()
    {
        _ticker?.Dispose();
        _ticker = new System.Threading.Timer(_ => Dispatcher.Invoke(OnTickSafe), null, 0, 250);
    }

    private void OnTickSafe()
    {
        var task = _task;
        if (task is null || task.Cancelled) return;

        var left = task.Target - DateTime.Now;
        if (left.TotalSeconds <= 0)
        {
            Fire(task);
            return;
        }
        UpdateState();
    }

    private void Fire(ScheduledTask task)
    {
        if (task.Cancelled) return;
        task.Cancelled = true;
        _ticker?.Dispose();
        _ticker = null;

        // 关机 / 重启已由系统计时，到点由系统执行，这里只收尾
        if (task.Action.UsesSystemTimer())
        {
            Finish("系统正在执行" + task.Action.ToDisplay() + "…");
            return;
        }

        var ok = PowerService.ExecuteNow(task.Action, out var err);
        Finish(ok ? $"已执行{task.Action.ToDisplay()}。" : err);
    }

    private void Finish(string message)
    {
        StateText.Text = message;
        CancelTaskButton.Visibility = Visibility.Collapsed;
        CloseClockByOwner();
        _task = null;
    }

    // ---------- 取消 ----------

    private void OnCancelTask(object sender, RoutedEventArgs e) => CancelTask();

    private void CancelTask(string message = "已取消该定时任务。")
    {
        var task = _task;
        if (task is null) return;

        task.Cancelled = true;
        _ticker?.Dispose();
        _ticker = null;

        if (task.Action.UsesSystemTimer())
            PowerService.AbortSystemShutdown(out _);

        _task = null;
        CloseClockByOwner();
        UpdateState(message);
    }

    /// <summary>由本程序主动关闭悬浮窗（不是用户点的关闭），不触发"关闭即取消"。</summary>
    private void CloseClockByOwner()
    {
        var clock = _clock;
        _clock = null;
        if (clock is null) return;

        clock.ClosedByOwner = true;
        try { clock.Close(); } catch { }
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        _clock?.SetStyle(SelectedStyle);
    }

    private void OnAbortSystem(object sender, RoutedEventArgs e)
    {
        if (PowerService.AbortSystemShutdown(out var err))
            HintText.Text = "已恢复（取消）系统已排定的关机。";
        else
            HintText.Text = "没有可取消的系统关机任务。";
    }

    private void UpdateState(string? overrideText = null)
    {
        if (overrideText is not null)
        {
            StateText.Text = overrideText;
            CancelTaskButton.Visibility = Visibility.Collapsed;
            return;
        }

        var task = _task;
        if (task is null || task.Cancelled)
        {
            StateText.Text = "尚未排定任务";
            CancelTaskButton.Visibility = Visibility.Collapsed;
            return;
        }

        var left = task.Target - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        StateText.Text =
            $"{task.Target:yyyy-MM-dd HH:mm} 将执行【{task.Action.ToDisplay()}】\n剩余 {FloatingClockWindow.Format(left)}";
        CancelTaskButton.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        _ticker?.Dispose();
        _ticker = null;
        CloseClockByOwner();
        base.OnClosed(e);
    }
}
