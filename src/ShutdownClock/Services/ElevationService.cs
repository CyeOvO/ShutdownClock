using System.Diagnostics;
using System.Security.Principal;

namespace ShutdownClock.Services;

/// <summary>管理员权限检测与自提升。</summary>
public static class ElevationService
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 以管理员权限重新启动自己。UAC 关闭或已提权时返回 false（表示"无需/无法再提权"）。
    /// </summary>
    public static bool RestartAsAdministrator()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception)
        {
            // 用户点了 UAC 的"否"
            return false;
        }
    }
}
