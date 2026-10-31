using FocusManager.Core.Time;

namespace FocusManager.Tests.TestSupport;

/// <summary>
/// 测试用的假时间源。
/// 真实程序用系统时钟，测试里必须能「想让它走多久就走多久」，
/// 否则测一次 90 分钟就要真的等 90 分钟。
/// </summary>
internal sealed class FakeTimeSource : ITimeSource
{
    public FakeTimeSource()
    {
        UtcNow = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
        MonotonicNow = TimeSpan.Zero;
    }

    public DateTimeOffset UtcNow { get; set; }

    public TimeSpan MonotonicNow { get; set; }

    /// <summary>时间正常流逝：单调时钟和墙上时钟一起往前走。</summary>
    public void Advance(TimeSpan delta)
    {
        MonotonicNow += delta;
        UtcNow += delta;
    }

    /// <summary>只让单调时钟走，墙上时钟不动（模拟系统时间被手动改慢）。</summary>
    public void AdvanceMonotonicOnly(TimeSpan delta) => MonotonicNow += delta;

    /// <summary>只让墙上时钟跳（模拟用户改系统时间、时区变化、NTP 校正）。</summary>
    public void JumpWallClock(TimeSpan delta) => UtcNow += delta;
}
