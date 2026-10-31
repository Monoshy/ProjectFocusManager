using System.Diagnostics;

namespace FocusManager.Core.Time;

/// <summary>
/// 真实环境使用的时间来源：UTC 时间 + 系统单调时钟（Stopwatch）。
/// </summary>
public sealed class SystemTimeSource : ITimeSource
{
    /// <summary>
    /// 程序启动时记下的一个基准刻度。之后所有单调时间都是「相对这个基准过了多久」。
    /// 用 Stopwatch 而不是 DateTime，是因为 Stopwatch 基于硬件高精度计数器，
    /// 不会因为系统对时、改时间、夏令时而跳变。
    /// </summary>
    private static readonly long OriginTimestamp = Stopwatch.GetTimestamp();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeSpan MonotonicNow => Stopwatch.GetElapsedTime(OriginTimestamp);
}
