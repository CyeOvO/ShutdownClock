using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShutdownClock.Models;

/// <summary>倒计时显示样式。</summary>
public enum CountdownStyle
{
    /// <summary>大字倒计时：HH:MM:SS 单行。</summary>
    BigDigits,
    /// <summary>进度环：环形进度 + 中心数字。</summary>
    ProgressRing,
    /// <summary>极简一行：只剩文字。</summary>
    Minimal
}

/// <summary>一次已排定的定时任务。</summary>
public sealed class ScheduledTask : INotifyPropertyChanged
{
    private DateTime _target;
    private PowerAction _action;
    private DateTime _createdAt = DateTime.Now;
    private bool _cancelled;

    public DateTime CreatedAt => _createdAt;

    /// <summary>目标时刻。</summary>
    public DateTime Target
    {
        get => _target;
        set { if (_target != value) { _target = value; OnPropertyChanged(); } }
    }

    public PowerAction Action
    {
        get => _action;
        set { if (_action != value) { _action = value; OnPropertyChanged(); } }
    }

    public bool Cancelled
    {
        get => _cancelled;
        set { if (_cancelled != value) { _cancelled = value; OnPropertyChanged(); } }
    }

    /// <summary>距目标还有多久（负数表示已到点）。</summary>
    public TimeSpan Remaining => _target - DateTime.Now;

    /// <summary>总时长，用于进度环计算。</summary>
    public TimeSpan Total => _target - _createdAt;

    public double Progress
    {
        get
        {
            var total = Total.TotalSeconds;
            if (total <= 0) return 1;
            var left = Math.Max(0, Remaining.TotalSeconds);
            return Math.Clamp(1 - left / total, 0, 1);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
