using System.Diagnostics;
using ShutdownClock.Models;

namespace ShutdownClock.Services;

/// <summary>
/// 电源操作入口。优先直接调用 Win32 API：实测在 WPF 进程中派生 shutdown.exe
/// 会稳定返回"拒绝访问(5)"，即使同一用户从控制台执行正常，因此不再依赖 shutdown.exe。
/// </summary>
public static class PowerService
{
    internal static void Log(string s)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "shutdownclock.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {s}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>排定关机 / 重启，交给系统计时（关掉本程序也生效）。</summary>
    public static bool ScheduleSystem(DateTime target, PowerAction action, out string error)
    {
        var restart = action == PowerAction.Restart;

        // 路径一：直接调用 Win32 API（权限足够时最稳）
        var ok = NativePower.ScheduleSystem(target, restart, out error);
        Log($"[ScheduleSystem/API] action={action} ok={ok} err={error}");
        if (ok) return true;

        // 路径二：回退到 shutdown.exe。
        // 实测普通用户下 API 会返回拒绝访问，但 shutdown.exe 能成功
        // （它内部走了一条对普通用户更宽松的路径）。
        Log("[ScheduleSystem] API 失败，回退到 shutdown.exe");
        var secs = (int)Math.Ceiling((target - DateTime.Now).TotalSeconds);
        if (secs < 0) secs = 0;
        var flag = restart ? "/r" : "/s";
        var note = $"ShutdownClock 定时{action.ToDisplay()} - {target:yyyy-MM-dd HH:mm}";
        var args = $"{flag} /f /t {secs} /c \"{note}\"";

        var r = RunExe("shutdown.exe", args);
        Log($"[ScheduleSystem/exe] exit={r.ExitCode} msg={r.Message}");
        if (r.Ok) return true;

        error = BuildFriendlyError(r, error);
        return false;
    }

    private readonly record struct ExeResult(bool Ok, int ExitCode, string Message);

    private static ExeResult RunExe(string file, string args)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // 关键（实测得出的两个约束）：
                //  1) 必须 UseShellExecute = true：WPF 进程里用 false 启动 shutdown.exe 会返回"拒绝访问(5)"或 1190；
                //  2) 不能同时设置 WindowStyle = Hidden：它会让上述调用重新落入 1190。
                // 因此这里只用 UseShellExecute = true + CreateNoWindow，且不重定向管道，改用退出码判定。
                var psi = new ProcessStartInfo(file, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = true
                };
                using var p = Process.Start(psi);
                if (p is null) return new ExeResult(false, -1, "无法启动 " + file);

                p.WaitForExit(15000);

                var msg = string.Empty;
                var res = new ExeResult(p.ExitCode == 0, p.ExitCode, msg);
                if (res.Ok || res.ExitCode != 5) return res;
                Thread.Sleep(200);   // 拒绝访问偶为瞬时状态
            }
            catch (Exception ex)
            {
                return new ExeResult(false, -1, ex.Message);
            }
        }
        return new ExeResult(false, 5, "拒绝访问");
    }

    private static string BuildFriendlyError(ExeResult r, string fallback)
    {
        if (r.ExitCode == 5)
            return "拒绝访问：系统拒绝了关机请求。请点上方“以管理员身份重启”后重试。";
        if (!string.IsNullOrWhiteSpace(r.Message)) return r.Message;
        return string.IsNullOrWhiteSpace(fallback) ? $"失败（返回码 {r.ExitCode}）" : fallback;
    }

    public static bool AbortSystemShutdown(out string error)
    {
        var ok = NativePower.AbortSystemShutdown(out error);
        Log($"[AbortSystemShutdown] ok={ok} err={error}");
        return ok;
    }

    /// <summary>立即执行动作。</summary>
    public static bool ExecuteNow(PowerAction action, out string error)
    {
        error = string.Empty;
        bool ok;
        switch (action)
        {
            case PowerAction.Shutdown:
                ok = NativePower.ShutdownNow(restart: false, out error);
                break;
            case PowerAction.Restart:
                ok = NativePower.ShutdownNow(restart: true, out error);
                break;
            case PowerAction.Hibernate:
                ok = NativePower.Suspend(hibernate: true, out error);
                break;
            case PowerAction.Sleep:
                ok = NativePower.Suspend(hibernate: false, out error);
                break;
            default:
                ok = true;
                break;
        }
        Log($"[ExecuteNow] action={action} ok={ok} err={error}");
        return ok;
    }
}