namespace ShutdownClock.Models;

/// <summary>到点后要执行的动作。</summary>
public enum PowerAction
{
    Shutdown,
    Restart,
    Hibernate,
    Sleep
}

public static class PowerActionInfo
{
    public static string ToDisplay(this PowerAction action) => action switch
    {
        PowerAction.Shutdown => "关机",
        PowerAction.Restart => "重启",
        PowerAction.Hibernate => "休眠",
        PowerAction.Sleep => "睡眠",
        _ => action.ToString()
    };

    /// <summary>关机和重启可以交给系统自己计时（关掉窗口也生效）。</summary>
    public static bool UsesSystemTimer(this PowerAction action)
        => action is PowerAction.Shutdown or PowerAction.Restart;
}
