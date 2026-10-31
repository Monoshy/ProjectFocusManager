namespace FocusManager.Core.Models;

/// <summary>
/// 随手记的一条想法。属于某个项目，可选地属于某次专注会话。
/// </summary>
public sealed class IdeaNote
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>记录这条想法时正在进行的会话。没有在专注时记录则为 null。</summary>
    public long? SessionId { get; set; }

    /// <summary>想法内容。</summary>
    public string Content { get; set; } = string.Empty;

    public IdeaNoteStatus Status { get; set; } = IdeaNoteStatus.Pending;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public enum IdeaNoteStatus
{
    /// <summary>待处理。</summary>
    Pending,

    /// <summary>已完成。</summary>
    Done
}

public static class IdeaNoteStatusExtensions
{
    public static string ToDbValue(this IdeaNoteStatus status) => status switch
    {
        IdeaNoteStatus.Pending => "pending",
        IdeaNoteStatus.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知的想法状态")
    };

    public static IdeaNoteStatus FromDbValue(string value) => value switch
    {
        "pending" => IdeaNoteStatus.Pending,
        "done" => IdeaNoteStatus.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "数据库中出现未知的想法状态")
    };
}
