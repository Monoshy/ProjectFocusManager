namespace FocusManager.Core.Models;

/// <summary>
/// 一段「有效专注时间」。用户暂停、结束时都会切分成段。
/// 这些段加起来就是本次会话的有效专注时间，暂停时间不在里面。
/// </summary>
public sealed class FocusInterval
{
    public long Id { get; set; }

    public long SessionId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset EndedAtUtc { get; set; }

    /// <summary>本段时长（秒）。存整秒，四舍五入。</summary>
    public long DurationSeconds { get; set; }
}
