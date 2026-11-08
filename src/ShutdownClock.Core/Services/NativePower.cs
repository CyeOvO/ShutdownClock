using System.Runtime.InteropServices;

namespace ShutdownClock.Services;

/// <summary>
/// 直接调用 Windows 关机 API。
/// 相比派生 shutdown.exe，这种方式不受进程上下文影响（实测在 WPF 进程中
/// 启动 shutdown.exe 会稳定返回"拒绝访问(5)"，即使同一用户从控制台执行正常）。
/// </summary>
internal static class NativePower
{
    private const int SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const int TOKEN_QUERY = 0x0008;

    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    // LUID 结构是 { DWORD LowPart; LONG HighPart }，必须用两个 32 位整数表示，
    // 用 long 会因为传参/对齐问题导致 LookupPrivilegeValue 返回 1313（找不到特权名）。
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokPriv1Luid
    {
        public int Count;
        public Luid Luid;
        public int Attr;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);

    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? host, string name, ref Luid pluid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall,
        ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("advapi32.dll", EntryPoint = "InitiateSystemShutdownExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool InitiateSystemShutdownEx(
        string? lpMachineName, string? lpMessage, uint dwTimeout,
        bool bForceAppsClosed, bool bRebootAfterShutdown, uint dwReason);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_FORCE = 0x00000004;
    private const uint EWX_POWEROFF = 0x00000008;
    private const uint EWX_FORCEIFHUNG = 0x00000010;

    private const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
    private const uint SHTDN_REASON_MINOR_OTHER = 0x00000000;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

    /// <summary>启用进程的关机特权（普通用户默认未启用）。</summary>
    private static bool EnableShutdownPrivilege(out string error)
    {
        error = string.Empty;
        var hproc = GetCurrentProcess();
        var htok = IntPtr.Zero;

        if (!OpenProcessToken(hproc, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref htok))
        {
            error = $"OpenProcessToken 失败（错误码 {Marshal.GetLastWin32Error()}）";
            return false;
        }

        var tp = new TokPriv1Luid { Count = 1, Attr = SE_PRIVILEGE_ENABLED, Luid = new Luid() };
        Marshal.SetLastPInvokeError(0);
        if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, ref tp.Luid))
        {
            var le = Marshal.GetLastWin32Error();
            error = $"LookupPrivilegeValue 失败（错误码 {le}，特权名 {SE_SHUTDOWN_NAME}，长度 {SE_SHUTDOWN_NAME.Length}）";
            return false;
        }

        if (!AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
        {
            error = $"AdjustTokenPrivileges 失败（错误码 {Marshal.GetLastWin32Error()}）";
            return false;
        }

        var lastErr = Marshal.GetLastWin32Error();
        if (lastErr == 1300)
        {
            // 1300 = 并非所有权限都已分配给调用者。此时不中断，
            // 继续尝试执行操作：能以普通权限完成的动作仍会成功。
            error = string.Empty;
            return true;
        }
        if (lastErr != 0)
        {
            error = $"启用关机特权失败（错误码 {lastErr}）";
            return false;
        }
        return true;
    }

    /// <summary>安排延时关机或重启。</summary>
    public static bool ScheduleSystem(DateTime target, bool restart, out string error)
    {
        error = string.Empty;
        var secs = (int)Math.Ceiling((target - DateTime.Now).TotalSeconds);
        if (secs < 0) secs = 0;

        // InitiateSystemShutdownExW 的 dwTimeout 单位是秒，最大受 OS 限制
        if (!EnableShutdownPrivilege(out error)) return false;

        const string message = "ShutdownClock 定时任务";
        var reason = SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED;

        var ok = InitiateSystemShutdownEx(null, message, (uint)secs, true, restart, reason);
        if (ok) return true;

        var code = Marshal.GetLastWin32Error();
        error = code switch
        {
            5 => "拒绝访问：需要管理员权限才能排定系统关机。",
            1116 => "没有进行中的关机过程。",
            _ => $"系统调用失败（错误码 {code}）"
        };
        return false;
    }

    /// <summary>取消已排定的关机。</summary>
    public static bool AbortSystemShutdown(out string error)
    {
        error = string.Empty;
        if (!EnableShutdownPrivilege(out error)) return false;

        if (AbortSystemShutdownNative(null))
        {
            var e = Marshal.GetLastWin32Error();
            if (e == 0) return true;
            error = e == 1116 ? "当前没有已排定的关机任务。" : $"错误码 {e}";
            return false;
        }
        var code = Marshal.GetLastWin32Error();
        error = code == 1116 ? "当前没有已排定的关机任务。" : $"取消失败（错误码 {code}）";
        return false;
    }

    [DllImport("advapi32.dll", EntryPoint = "AbortSystemShutdownW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AbortSystemShutdownNative(string? lpMachineName);

    /// <summary>立即关机 / 重启。</summary>
    public static bool ShutdownNow(bool restart, out string error)
    {
        error = string.Empty;
        if (!EnableShutdownPrivilege(out error)) return false;

        var flags = restart ? EWX_REBOOT : EWX_SHUTDOWN;
        flags |= EWX_POWEROFF | EWX_FORCEIFHUNG;

        if (ExitWindowsEx(flags, SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED))
            return true;

        var code = Marshal.GetLastWin32Error();
        error = code == 5 ? "拒绝访问：需要管理员权限。" : $"关机失败（错误码 {code}）";
        return false;
    }

    /// <summary>休眠 / 睡眠。hibernate=false 时为睡眠。</summary>
    public static bool Suspend(bool hibernate, out string error)
    {
        error = string.Empty;
        if (SetSuspendState(hibernate, false, false)) return true;

        var code = Marshal.GetLastWin32Error();
        error = code == 5
            ? "拒绝访问：需要管理员权限。"
            : $"操作失败（错误码 {code}）。若休眠不可用，请用管理员执行：powercfg /hibernate on";
        return false;
    }
}
