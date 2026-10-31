namespace FocusManager.Core.Models;

/// <summary>
/// 一次暂停记录。用户点「暂停」时必须填写原因，原因会保存在这里。
/// </summary>
public sealed class PauseRecord
{
    public long Id { get; set; }

    public long SessionId { get; set; }

    /// <summary>暂停原因，例如「喝水」「临时有事」「查找资料」。</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>暂停结束时间。还在暂停中时为 null。</summary>
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>暂停时长（秒）。还在暂停中时为 null。</summary>
    public long? DurationSeconds { get; set; }
}
