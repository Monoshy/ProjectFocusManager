using System.Globalization;

namespace FocusManager.Core.Services;

/// <summary>
/// 「暂停多久之后提醒一次」的规则。
///
/// 只做两件纯计算的事：把设置里的分钟数翻译成阈值；判断现在该不该提醒。
/// 没有计时器、没有窗口，方便单独测试。
/// </summary>
public static class PauseReminderRule
{
    /// <summary>设置项的名字。</summary>
    public const string SettingKey = "PauseReminderMinutes";

    /// <summary>默认 5 分钟。</summary>
    public const int DefaultMinutes = 5;

    /// <summary>界面上允许选择的分钟数。0 表示不提醒。</summary>
    public static readonly int[] AllowedMinutes = [5, 10, 15, 0];

    /// <summary>
    /// 把设置里存的字符串翻译成阈值。空值、看不懂的值都退回默认 5 分钟；
    /// 0 表示用户关掉了这个提醒，返回 null。
    /// </summary>
    public static TimeSpan? ResolveThreshold(string? storedValue)
    {
        var minutes = DefaultMinutes;

        if (!string.IsNullOrWhiteSpace(storedValue)
            && int.TryParse(storedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0
            && parsed <= 24 * 60)
        {
            minutes = parsed;
        }

        return minutes == 0 ? null : TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// 现在该不该弹提醒。已经提醒过就不再提醒；阈值被关掉（null）也不提醒。
    /// </summary>
    public static bool ShouldRemind(TimeSpan paused, TimeSpan? threshold, bool alreadyReminded)
    {
        if (alreadyReminded)
            return false;

        if (threshold is null)
            return false;

        return paused >= threshold.Value;
    }
}
