namespace FocusManager.Core.Models;

/// <summary>
/// 会话心跳快照。程序每隔一段时间记一次当前进度，
/// 这样即使程序被强制结束，也知道「上次保存到哪」。
/// </summary>
public sealed class SessionCheckpoint
{
    public long Id { get; set; }

    public long SessionId { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>记录这一刻，本次会话已经累计的有效专注秒数。</summary>
    public long EffectiveSeconds { get; set; }

    /// <summary>记录这一刻的会话状态（running / paused）。</summary>
    public string State { get; set; } = string.Empty;
}
