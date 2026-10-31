namespace FocusManager.Core.Models;

/// <summary>
/// 一次专注会话。一次会话可以包含多个「有效专注段」和多个「暂停段」。
/// </summary>
public sealed class FocusSession
{
    public long Id { get; set; }

    /// <summary>所属项目。</summary>
    public long ProjectId { get; set; }

    /// <summary>本轮选择的主要学习应用（可以为空，例如还没选应用）。</summary>
    public long? PrimaryApplicationId { get; set; }

    /// <summary>会话开始时间（UTC）。</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>会话结束时间（UTC）。未结束时为 null。</summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    public SessionStatus Status { get; set; }

    /// <summary>有效专注秒数（= 该会话所有 FocusIntervals 之和）。</summary>
    public long EffectiveSeconds { get; set; }

    /// <summary>暂停总秒数（= 该会话所有 PauseRecords 之和）。</summary>
    public long PausedSeconds { get; set; }

    /// <summary>暂停次数。</summary>
    public int PauseCount { get; set; }

    /// <summary>「本轮完成了什么？」必填。</summary>
    public string? ProgressNote { get; set; }

    /// <summary>「有什么心得或还没有解决的问题？」选填。</summary>
    public string? ReflectionNote { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public TimeSpan EffectiveTime => TimeSpan.FromSeconds(EffectiveSeconds);

    public TimeSpan PausedTime => TimeSpan.FromSeconds(PausedSeconds);
}
