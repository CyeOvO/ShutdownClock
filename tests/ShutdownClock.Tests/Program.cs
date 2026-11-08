using ShutdownClock.Models;

namespace ShutdownClock.Tests;

/// <summary>极简测试运行器：不依赖外部测试框架，dotnet run 即可验证核心逻辑。</summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {name}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {name}  {detail}"); }
    }

    private static void Main()
    {
        var now = new DateTime(2026, 11, 8, 10, 0, 0);

        Console.WriteLine("TimeInputParser：相对时间");
        var r = TimeInputParser.Parse("+90", now);
        Check("+90 = 90 分钟后", r.Ok && r.Time == now.AddMinutes(90), $"{r.Time}");

        r = TimeInputParser.Parse("+2h", now);
        Check("+2h = 2 小时后", r.Ok && r.Time == now.AddHours(2), $"{r.Time}");

        r = TimeInputParser.Parse("+30s", now);
        Check("+30s = 30 秒后", r.Ok && r.Time == now.AddSeconds(30), $"{r.Time}");

        r = TimeInputParser.Parse("+0", now);
        Check("+0 被拒绝", !r.Ok, r.Message);

        Console.WriteLine("TimeInputParser：只写时刻");
        r = TimeInputParser.Parse("23:30", now);
        Check("23:30 = 今天 23:30", r.Ok && r.Time == new DateTime(2026, 11, 8, 23, 30, 0), $"{r.Time}");

        r = TimeInputParser.Parse("08:00", now);
        Check("08:00 已过 -> 顺延到明天", r.Ok && r.Time == new DateTime(2026, 11, 9, 8, 0, 0), $"{r.Time}");

        r = TimeInputParser.Parse("25:00", now);
        Check("25:00 被拒绝", !r.Ok, r.Message);

        r = TimeInputParser.Parse("23:60", now);
        Check("23:60 被拒绝", !r.Ok, r.Message);

        Console.WriteLine("TimeInputParser：完整日期");
        r = TimeInputParser.Parse("2026-11-10 23:30", now);
        Check("yyyy-MM-dd HH:mm", r.Ok && r.Time == new DateTime(2026, 11, 10, 23, 30, 0), $"{r.Time}");

        r = TimeInputParser.Parse("2026/11/10 23:30", now);
        Check("yyyy/MM/dd HH:mm", r.Ok && r.Time == new DateTime(2026, 11, 10, 23, 30, 0), $"{r.Time}");

        r = TimeInputParser.Parse("2026-11-10 23:30:45", now);
        Check("带秒的格式", r.Ok && r.Time == new DateTime(2026, 11, 10, 23, 30, 45), $"{r.Time}");

        r = TimeInputParser.Parse("2026-11-01 10:00", now);
        Check("过去的时间被拒绝", !r.Ok && r.Reason == TimeInputParser.FailureReason.Past, r.Message);

        Console.WriteLine("TimeInputParser：异常输入");
        foreach (var bad in new[] { "", "   ", "abc", "2026-13-01 10:00", "11/10/2026 23:30", "23.30" })
        {
            r = TimeInputParser.Parse(bad, now);
            Check($"'{bad}' 被拒绝", !r.Ok, r.Message);
        }

        r = TimeInputParser.Parse("２３:３０", now);
        Check("全角数字被识别为全角错误", !r.Ok && r.Reason == TimeInputParser.FailureReason.FullWidth, r.Message);

        r = TimeInputParser.Parse("23：30", now);
        Check("全角冒号被识别为全角错误", !r.Ok && r.Reason == TimeInputParser.FailureReason.FullWidth, r.Message);

        Console.WriteLine("ScheduledTask");
        var task = new ScheduledTask { Target = DateTime.Now.AddMinutes(10), Action = PowerAction.Shutdown };
        Check("Remaining 为正", task.Remaining.TotalMinutes > 9 && task.Remaining.TotalMinutes <= 10);
        Check("Progress 在 0..1 之间", task.Progress >= 0 && task.Progress <= 1);

        Console.WriteLine("PowerAction");
        Check("关机使用系统计时", PowerAction.Shutdown.UsesSystemTimer());
        Check("重启使用系统计时", PowerAction.Restart.UsesSystemTimer());
        Check("休眠不使用系统计时", !PowerAction.Hibernate.UsesSystemTimer());
        Check("睡眠不使用系统计时", !PowerAction.Sleep.UsesSystemTimer());
        Check("中文显示名", PowerAction.Shutdown.ToDisplay() == "关机");


        Console.WriteLine("CountdownStyle");
        var styles = Enum.GetNames<CountdownStyle>();
        Check("共 3 种样式（翻页已移除）", styles.Length == 3, string.Join(",", styles));
        Check("不含 FlipClock", !styles.Contains("FlipClock"));
        Check("默认样式为 BigDigits", Enum.GetValues<CountdownStyle>()[0] == CountdownStyle.BigDigits);
        Console.WriteLine();
        Console.WriteLine($"通过 {_pass}，失败 {_fail}");
        Environment.Exit(_fail == 0 ? 0 : 1);
    }
}