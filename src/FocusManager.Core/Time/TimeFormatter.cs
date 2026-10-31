namespace FocusManager.Core.Time;

/// <summary>
/// 把时长格式化成界面上显示的文字。全部使用中文习惯。
/// </summary>
public static class TimeFormatter
{
    /// <summary>
    /// 计时器大字显示：01:25:38（时:分:秒）。超过 99 小时也不会截断。
    /// </summary>
    public static string ToClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        var totalHours = (long)value.TotalHours;
        return $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    /// <summary>
    /// 中文自然语言显示，例如：
    /// 45 秒 → 「45 秒」
    /// 12 分钟 → 「12 分钟」
    /// 85 分钟 → 「1 小时 25 分钟」
    /// 3 小时整 → 「3 小时」
    /// </summary>
    public static string ToChinese(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;

        var totalSeconds = (long)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);
        if (totalSeconds == 0) return "0 秒";

        if (totalSeconds < 60) return $"{totalSeconds} 秒";

        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;

        if (hours > 0)
        {
            return minutes == 0
                ? $"{hours} 小时"
                : $"{hours} 小时 {minutes} 分钟";
        }

        return seconds == 0
            ? $"{minutes} 分钟"
            : $"{minutes} 分钟 {seconds} 秒";
    }

    /// <summary>
    /// 日期显示：2026年10月31日 14:36
    /// </summary>
    public static string ToLocalDateTimeText(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return $"{local.Year}年{local.Month}月{local.Day}日 {local.Hour:00}:{local.Minute:00}";
    }

    /// <summary>
    /// 只显示本地日期：10月31日
    /// </summary>
    public static string ToLocalDateText(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return $"{local.Month}月{local.Day}日";
    }
}
