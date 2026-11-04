using FocusManager.Core.Time;

namespace FocusManager.Core.Services;

/// <summary>
/// 「这一轮值不值得记下来」的判断规则。
///
/// 来自需求：专注时间不到 5 分钟的，不记进历史记录。
/// 理由很实际——刚点开始就发现选错了项目、或者临时被叫走，这种碎片不该污染统计，
/// 也不该让「每次专注的记录」里塞满几十秒的条目。
///
/// 规则只对「用户主动结束」生效：程序崩溃后恢复出来的那一轮，
/// 已经发生的时间照样保留（它可能是真实专注了很久、只是没来得及正常结束）。
/// </summary>
public static class SessionRecordingRules
{
    /// <summary>有效专注时间达到这个秒数才记录。</summary>
    public const long MinimumEffectiveSeconds = 300;

    /// <summary>这一轮有效专注够不够 5 分钟。</summary>
    public static bool ShouldRecord(long effectiveSeconds)
        => effectiveSeconds >= MinimumEffectiveSeconds;

    /// <summary>这一轮有效专注够不够 5 分钟。</summary>
    public static bool ShouldRecord(TimeSpan effective)
        => ShouldRecord((long)Math.Round(effective.TotalSeconds, MidpointRounding.AwayFromZero));

    /// <summary>作废之后给用户看的一句话。</summary>
    public static string BuildDiscardMessage(long effectiveSeconds)
    {
        var duration = TimeFormatter.ToChinese(TimeSpan.FromSeconds(Math.Max(0, effectiveSeconds)));

        return $"这一轮有效专注 {duration}，不到 5 分钟，已经作废、没有记进历史。";
    }
}
