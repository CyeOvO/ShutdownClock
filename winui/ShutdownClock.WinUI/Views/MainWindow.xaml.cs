using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ShutdownClock.Models;
using ShutdownClock.Services;
using Windows.System;
using Windows.UI;

namespace ShutdownClock.Views;

public sealed partial class MainWindow : Window
{
    private FloatingClockWindow? _clock;
    private ScheduledTask? _task;
    private System.Threading.Timer? _ticker;
    private bool _shuttingDown;

    public MainWindow()
    {
        InitializeComponent();

        Title = "ShutdownClock · 定时关机 / 休眠";

        WindowHelper.SetSize(this, 620, 760);
        WindowHelper.CenterOnScreen(this);

        // 系统支持就用 Mica 材质，不支持则用纯色背景兜底
        if (!WindowHelper.TryApplyMica(this))
        {
            Theme.Refresh();
            PageBackground.Color = Theme.WindowBackground;
        }

        TimeBox.Text = DateTime.Now.AddMinutes(30).ToString("yyyy-MM-dd HH:mm");

        // 清单已声明 requireAdministrator，正常情况总是已提权；保守起见仍做检测
        if (!ElevationService.IsAdministrator())
            ElevateBar.IsOpen = true;

        ApplyCardBackgrounds();
        UpdateState();

        // 关键：主窗口被关闭（点 ×、Alt+F4、任务管理器结束）时必须收掉悬浮窗。
        // 之前没有挂这个事件，导致主窗关掉后悬浮窗变成孤儿窗口、一直挂在桌面上关不掉。
        Closed += (_, _) => Shutdown();
    }

    /// <summary>卡片背景跟随系统深浅色。</summary>
    private void ApplyCardBackgrounds()
    {
        Theme.Refresh();
        var brush = new SolidColorBrush(Theme.CardBackground);
        foreach (var card in new[] { ActionCard, TimeCard, StyleCard, StateCard })
            card.Background = brush;

        if (!WindowHelper.IsMicaActive(this))
            PageBackground.Color = Theme.WindowBackground;
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
            CloseClockByOwner();
        }

        if (ElevationService.RestartAsAdministrator())
            Application.Current.Exit();
        else
            HintText.Text = "已取消提权（可能点了 UAC 的否）。";
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

    private void OnTimeBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            OnStartClick(sender, e);
        }
    }

    // ---------- 开始 ----------

    private void OnStartClick(object sender, object e)
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
        if (_clock is null)
        {
            _clock = new FloatingClockWindow();
            _clock.CancelRequested += (_, _) => CancelTask();
            _clock.WindowClosed += OnClockClosedByUser;
        }

        _clock.SetStyle(SelectedStyle);
        _clock.AttachTask(task);
        _clock.Activate();
    }

    /// <summary>用户直接关掉悬浮窗 = 取消本次排定，主窗倒计时同步停止。</summary>
    private void OnClockClosedByUser(object? sender, EventArgs e)
    {
        _clock = null;

        // 主程序正在退出时不需要再改界面，也不需要再撤销一次
        if (_shuttingDown) return;

        if (_task is not null && !_task.Cancelled)
            CancelTask("已关闭倒计时窗口，定时任务已取消。");
    }

    private void StartTicker()
    {
        _ticker?.Dispose();
        _ticker = new System.Threading.Timer(_ => OnTickSafe(), null, 0, 250);
    }

    private void OnTickSafe()
    {
        // Timer 回调在后台线程，必须切回 UI 线程
        RootGrid.DispatcherQueue.TryEnqueue(() =>
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
        });
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
        clock?.CloseByOwner();
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        _clock?.SetStyle(SelectedStyle);
    }

    private void OnAbortSystem(object sender, RoutedEventArgs e)
    {
        if (PowerService.AbortSystemShutdown(out _))
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

    /// <summary>主窗口关闭时一并收掉悬浮窗与计时器，避免留下孤儿窗口。</summary>
    public void Shutdown()
    {
        _shuttingDown = true;

        _ticker?.Dispose();
        _ticker = null;

        // 主程序退出：若还有未执行的排定，先把系统计时器撤掉，
        // 否则窗口关了但关机仍在队列里，用户会以为"取消"没生效。
        var task = _task;
        if (task is not null && !task.Cancelled && task.Action.UsesSystemTimer())
            PowerService.AbortSystemShutdown(out _);
        if (task is not null) task.Cancelled = true;
        _task = null;

        CloseClockByOwner();
    }
}
