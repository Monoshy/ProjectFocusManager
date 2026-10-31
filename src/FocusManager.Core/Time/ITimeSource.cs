namespace FocusManager.Core.Time;

/// <summary>
/// 时间来源。把「读时间」这件事抽出来，是为了两个目的：
/// 1. 生产环境用真实的单调时钟（不受系统时间被修改影响）；
/// 2. 单元测试可以手动推进时间，不需要真的等 10 分钟。
/// </summary>
public interface ITimeSource
{
    /// <summary>当前 UTC 时间。会被写入数据库，用于记录「几点几分」。</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// 单调递增的时间刻度。只用于计算「过了多久」，绝不写入数据库。
    /// 它的特点是：即使系统时间被手动调整、被时区切换影响，它也不会跳变。
    /// </summary>
    TimeSpan MonotonicNow { get; }
}
