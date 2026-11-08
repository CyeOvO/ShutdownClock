using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        TimeBox.Text = DateTime.Now.AddMinutes(30).ToString("yyyy-MM-dd HH:mm");
        // 清单已声明 requireAdministrator，正常情况总是已提权；保守起见仍做检测
        if (!ElevationService.IsAdministrator()) ElevateBar.Visibility = Visibility.Visible;
        UpdateState();
    }

    private PowerAction SelectedAction =>
        RbRestart.IsChecked == true ? PowerAction.Restart :
        RbHibernate.IsChecked == true ? PowerAction.Hibernate :
        RbSleep.IsChecked == true ? PowerAction.Sleep :
        PowerAction.Shutdown;

    private CountdownStyle SelectedStyle => StyleBox.SelectedIndex switch
    {
        1 => CountdownStyle.BigDigits,
        2 => CountdownStyle.ProgressRing,
        3 => CountdownStyle.Minimal,
        _ => CountdownStyle.FlipClock
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
                _clock = null;
                // 悬浮窗被关掉但任务还在跑：关机/重启仍由系统计时，其它动作已无人等待，需提示
                if (_task is not null && !_task.Cancelled && !_task.Action.UsesSystemTimer())
                {
                    HintText.Text = "悬浮窗已关闭，休眠/睡眠已无法自动执行，请取消后重设。";
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
        try
        {
            if (_clock is not null) _clock.Close();
        }
        catch { }
        _clock = null;
        _task = null;
    }

    // ---------- 取消 ----------

    private void OnCancelTask(object sender, RoutedEventArgs e) => CancelTask();

    private void CancelTask()
    {
        var task = _task;
        if (task is null) return;

        task.Cancelled = true;
        _ticker?.Dispose();
        _ticker = null;

        if (task.Action.UsesSystemTimer())
            PowerService.AbortSystemShutdown(out _);

        _task = null;
        try { _clock?.Close(); } catch { }
        _clock = null;
        UpdateState("已取消该定时任务。");
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
        try { _clock?.Close(); } catch { }
        base.OnClosed(e);
    }
}
