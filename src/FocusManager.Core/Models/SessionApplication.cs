namespace FocusManager.Core.Models;

/// <summary>
/// 某一轮专注实际用到的程序（含主应用）。
///
/// 为什么要存名称和进程名的副本，而不是只存一个 ApplicationId：
/// 应用以后可能被用户删掉，但「那一轮用的是什么」属于历史记录，不能跟着消失。
/// 所以这里存快照，ApplicationId 只是「当时对应的那条应用记录」的线索。
/// </summary>
public sealed class SessionApplication
{
    public long Id { get; set; }

    public long SessionId { get; set; }

    /// <summary>对应的应用编号。应用被删除后这里是 null。</summary>
    public long? ApplicationId { get; set; }

    /// <summary>当时的显示名称。</summary>
    public string NameSnapshot { get; set; } = string.Empty;

    /// <summary>当时的进程名（不含 .exe），用来判断前台窗口算不算允许。</summary>
    public string ProcessNameSnapshot { get; set; } = string.Empty;

    /// <summary>是不是这一轮的主要学习应用。</summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }

    public override string ToString() => NameSnapshot;
}
