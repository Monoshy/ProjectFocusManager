namespace FocusManager.Core.Models;

/// <summary>
/// 一次专注会话的状态。
/// </summary>
public enum SessionStatus
{
    /// <summary>正在专注计时中。</summary>
    Running,

    /// <summary>用户主动暂停中。</summary>
    Paused,

    /// <summary>用户正常结束，并且填写了总结。</summary>
    Completed,

    /// <summary>异常结束（程序崩溃、强制关闭等），时间仍然保留。</summary>
    Aborted,

    /// <summary>程序上次异常退出后，由用户在启动提示里选择「结束」的会话。</summary>
    Recovered
}

public static class SessionStatusExtensions
{
    /// <summary>把状态转成数据库里保存的文本。</summary>
    public static string ToDbValue(this SessionStatus status) => status switch
    {
        SessionStatus.Running => "running",
        SessionStatus.Paused => "paused",
        SessionStatus.Completed => "completed",
        SessionStatus.Aborted => "aborted",
        SessionStatus.Recovered => "recovered",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "未知的会话状态")
    };

    /// <summary>从数据库文本还原状态。</summary>
    public static SessionStatus FromDbValue(string value) => value switch
    {
        "running" => SessionStatus.Running,
        "paused" => SessionStatus.Paused,
        "completed" => SessionStatus.Completed,
        "aborted" => SessionStatus.Aborted,
        "recovered" => SessionStatus.Recovered,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "数据库中出现未知的会话状态")
    };

    /// <summary>是否为「还没结束」的状态（程序异常退出后需要向用户询问）。</summary>
    public static bool IsUnfinished(this SessionStatus status)
        => status is SessionStatus.Running or SessionStatus.Paused;
}
