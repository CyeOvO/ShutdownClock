using System.Globalization;
using System.Text.RegularExpressions;

namespace ShutdownClock.Models;

/// <summary>把用户输入的时间文本解析成绝对时间。</summary>
public static class TimeInputParser
{
    public enum FailureReason
    {
        None,
        Empty,
        FullWidth,
        Format,
        Past
    }

    public readonly record struct Result(bool Ok, DateTime? Time, FailureReason Reason, string? Message)
    {
        public static Result Success(DateTime t) => new(true, t, FailureReason.None, null);
        public static Result Fail(FailureReason r, string msg) => new(false, null, r, msg);
    }

    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy-M-d H:m",
        "yyyy/M/d H:m"
    };

    public static Result Parse(string? raw, DateTime now)
    {
        var v = (raw ?? string.Empty).Trim();
        if (v.Length == 0)
            return Result.Fail(FailureReason.Empty, "请输入时间。");

        // 全角字符：中文输入法常见的坑，单独提示
        foreach (var ch in v)
        {
            if (ch < 0x20 || ch > 0x7E)
                return Result.Fail(FailureReason.FullWidth,
                    "检测到全角字符。请把输入法切到英文半角后重试，冒号必须是半角的 : 而不是中文的 ：");
        }

        // C) 相对分钟  +90
        var rel = Regex.Match(v, @"^\+\s*(\d+)$");
        if (rel.Success)
        {
            if (!int.TryParse(rel.Groups[1].Value, out var mins) || mins <= 0)
                return Result.Fail(FailureReason.Format, "相对分钟数必须大于 0。");
            return Result.Success(now.AddMinutes(mins));
        }

        // 相对小时  +2h   / 相对秒 +30s
        var relH = Regex.Match(v, @"^\+\s*(\d+)\s*[hH]$");
        if (relH.Success && int.TryParse(relH.Groups[1].Value, out var hrs) && hrs > 0)
            return Result.Success(now.AddHours(hrs));

        var relS = Regex.Match(v, @"^\+\s*(\d+)\s*[sS]$");
        if (relS.Success && int.TryParse(relS.Groups[1].Value, out var secs) && secs > 0)
            return Result.Success(now.AddSeconds(secs));

        // B) 只写时刻  23:30
        var tm = Regex.Match(v, @"^(\d{1,2}):(\d{2})(?::(\d{2}))?$");
        if (tm.Success)
        {
            var hh = int.Parse(tm.Groups[1].Value);
            var mm = int.Parse(tm.Groups[2].Value);
            var ss = tm.Groups[3].Success ? int.Parse(tm.Groups[3].Value) : 0;
            if (hh > 23 || mm > 59 || ss > 59)
                return Result.Fail(FailureReason.Format, "时刻不合法：小时 0-23，分钟和秒 0-59。");

            var t = now.Date.AddHours(hh).AddMinutes(mm).AddSeconds(ss);
            if (t <= now) t = t.AddDays(1);   // 今天已过就顺延到明天
            return Result.Success(t);
        }

        // A) 完整日期时间
        if (DateTime.TryParseExact(v, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            if (dt < now)
                return Result.Fail(FailureReason.Past, "这个时间已经过去了，请重新输入。");
            return Result.Success(dt);
        }

        return Result.Fail(FailureReason.Format,
            "无法识别。支持：2026-11-10 23:30 / 23:30 / +90（分钟）/ +2h / +30s");
    }
}
