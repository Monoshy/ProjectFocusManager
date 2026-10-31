using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 计时核心的测试。对应验收清单里的「计时测试」。
///
/// 这里全部使用假时间源：想测 90 分钟就「假装」过了 90 分钟，
/// 不用真的等。同时也能模拟系统时间被修改、电脑休眠等异常情况。
/// </summary>
public sealed class FocusTimerServiceTests
{
    [Fact]
    public void Start_ThenTimePasses_ReportsAccurateDuration()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(90));

        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(90), timer.EffectiveDuration);
        Assert.Equal(TimeSpan.Zero, timer.PausedDuration);
    }

    [Fact]
    public void Pause_ExcludesPausedTimeFromEffectiveDuration()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(10));   // 专注 10 分钟
        timer.Pause("喝水");
        time.Advance(TimeSpan.FromMinutes(5));    // 暂停 5 分钟
        timer.Resume();
        time.Advance(TimeSpan.FromMinutes(20));   // 再专注 20 分钟

        Assert.Equal(TimeSpan.FromMinutes(30), timer.EffectiveDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.PausedDuration);

        var result = timer.Stop();

        Assert.Equal(30 * 60, result.EffectiveSeconds);
        Assert.Equal(5 * 60, result.PausedSeconds);
        Assert.Equal(1, result.PauseCount);
        Assert.Equal(2, result.IntervalCount);
    }

    [Fact]
    public void MultiplePauses_AreSummedCorrectly()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();

        for (var i = 1; i <= 3; i++)
        {
            time.Advance(TimeSpan.FromMinutes(10));  // 每段专注 10 分钟
            timer.Pause($"原因{i}");
            time.Advance(TimeSpan.FromMinutes(3));   // 每次暂停 3 分钟
            timer.Resume();
        }

        time.Advance(TimeSpan.FromMinutes(10));      // 最后再专注 10 分钟

        var result = timer.Stop();

        Assert.Equal(4, result.IntervalCount);
        Assert.Equal(3, result.PauseCount);
        Assert.Equal(40 * 60, result.EffectiveSeconds);
        Assert.Equal(9 * 60, result.PausedSeconds);
    }

    [Fact]
    public void Pause_RequiresReason()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);
        timer.Start();

        Assert.Throws<ArgumentException>(() => timer.Pause(""));
        Assert.Throws<ArgumentException>(() => timer.Pause("   "));

        // 抛异常后不应该把状态搞坏，仍然可以正常暂停
        timer.Pause("喝水");
        Assert.True(timer.IsPaused);
        Assert.Equal("喝水", timer.CurrentPauseReason);
    }

    [Fact]
    public void StopWhilePaused_CountsPauseAndKeepsEffectiveTime()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(12));
        timer.Pause("临时有事");
        time.Advance(TimeSpan.FromMinutes(8));

        var result = timer.Stop();

        Assert.Equal(TimerState.Stopped, timer.State);
        Assert.Equal(12 * 60, result.EffectiveSeconds);
        Assert.Equal(8 * 60, result.PausedSeconds);
        Assert.Equal(1, result.PauseCount);
    }

    [Fact]
    public void WallClockJump_DoesNotAffectEffectiveDuration()
    {
        // 模拟：用户改系统时间、或电脑从休眠恢复后系统时间被校正。
        // 有效专注时间必须只由单调时钟决定，不能被墙上时钟带偏。
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        time.JumpWallClock(TimeSpan.FromHours(2));   // 系统时间突然跳快 2 小时

        Assert.Equal(TimeSpan.FromMinutes(10), timer.EffectiveDuration);

        var result = timer.Stop();
        Assert.Equal(10 * 60, result.EffectiveSeconds);
    }

    [Fact]
    public void Resume_ContinuesFromWhereItLeftOff()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(5));
        timer.Pause("查资料");
        time.Advance(TimeSpan.FromMinutes(30));   // 暂停很久也不会自动恢复
        timer.Resume();
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(10), timer.EffectiveDuration);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.PausedDuration);
    }

    [Fact]
    public void Intervals_AreSplitAtPauseBoundaries()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        var pushed = new List<FocusIntervalSnapshot>();
        timer.IntervalCompleted += (_, snapshot) => pushed.Add(snapshot);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        timer.Pause("喝水");                      // 第一段在这里结束
        time.Advance(TimeSpan.FromMinutes(2));
        timer.Resume();
        time.Advance(TimeSpan.FromMinutes(15));
        timer.Stop();                              // 第二段在这里结束

        Assert.Equal(2, pushed.Count);
        Assert.Equal(TimeSpan.FromMinutes(10), pushed[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(15), pushed[1].Duration);
    }

    [Fact]
    public void Pauses_AreRecordedWithReasonAndDuration()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        var pushed = new List<PauseSnapshot>();
        timer.PauseCompleted += (_, snapshot) => pushed.Add(snapshot);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(3));
        timer.Pause("查找资料");
        time.Advance(TimeSpan.FromMinutes(7));
        timer.Resume();

        var pause = Assert.Single(pushed);
        Assert.Equal("查找资料", pause.Reason);
        Assert.Equal(TimeSpan.FromMinutes(7), pause.Duration);
    }

    [Fact]
    public void FlushCurrentInterval_PersistsProgressWithoutLosingTime()
    {
        // 心跳：把正在进行的这一段先存下来，然后接着计时。
        // 总时间不能因此变多或变少。
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        var pushed = new List<FocusIntervalSnapshot>();
        timer.IntervalCompleted += (_, snapshot) => pushed.Add(snapshot);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(5));
        timer.FlushCurrentInterval(TimeSpan.FromSeconds(30));

        Assert.Single(pushed);
        Assert.Equal(TimeSpan.FromMinutes(5), pushed[0].Duration);

        time.Advance(TimeSpan.FromMinutes(5));
        var result = timer.Stop();

        Assert.Equal(10 * 60, result.EffectiveSeconds);
        Assert.Equal(2, result.IntervalCount);
    }

    [Fact]
    public void FlushCurrentInterval_IgnoresVeryShortSegments()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        var pushed = new List<FocusIntervalSnapshot>();
        timer.IntervalCompleted += (_, snapshot) => pushed.Add(snapshot);

        timer.Start();
        time.Advance(TimeSpan.FromSeconds(5));
        timer.FlushCurrentInterval(TimeSpan.FromSeconds(30));

        Assert.Empty(pushed);
        Assert.Equal(TimeSpan.FromSeconds(5), timer.EffectiveDuration);
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();

        var error = Assert.Throws<InvalidOperationException>(() => timer.Start());
        Assert.Contains("已经", error.Message);
    }

    [Fact]
    public void Pause_WithoutStart_Throws()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        Assert.Throws<InvalidOperationException>(() => timer.Pause("喝水"));
    }

    [Fact]
    public void Stop_WithoutStart_Throws()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        Assert.Throws<InvalidOperationException>(() => timer.Stop());
    }

    [Fact]
    public void Resume_WhenNotPaused_Throws()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        Assert.Throws<InvalidOperationException>(() => timer.Resume());
    }

    [Fact]
    public void Stop_Twice_Throws()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(1));
        timer.Stop();

        Assert.Throws<InvalidOperationException>(() => timer.Stop());
    }

    [Fact]
    public void StartAfterStop_BeginsNewSessionFromZero()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(20));
        timer.Stop();

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(TimeSpan.FromMinutes(3), timer.EffectiveDuration);
        Assert.Equal(TimeSpan.Zero, timer.PausedDuration);
        Assert.Equal(0, timer.PauseCount);
    }

    [Fact]
    public void SecondsAreRoundedToNearestWholeSecond()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMilliseconds(1499));   // 1.499 秒
        var result = timer.Stop();

        Assert.Equal(1, result.EffectiveSeconds);
    }

    // ===== 冻结（点「结束」后弹总结窗口的那段时间不该算专注时间） =====

    [Fact]
    public void Freeze_StopsAccumulatingEffectiveDuration()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(20));

        timer.Freeze();
        time.Advance(TimeSpan.FromMinutes(3));   // 在总结窗口里写了 3 分钟

        Assert.True(timer.IsFrozen);
        Assert.Equal(TimerState.Running, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(20), timer.EffectiveDuration);
    }

    [Fact]
    public void Stop_AfterFreeze_ReportsTimeUpToFreezeMoment()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        var startedAt = timer.StartedAtUtc;
        time.Advance(TimeSpan.FromMinutes(20));
        var frozenAt = time.UtcNow;

        timer.Freeze();
        time.Advance(TimeSpan.FromMinutes(3));   // 写总结

        var result = timer.Stop();

        Assert.Equal(TimeSpan.FromMinutes(20), result.EffectiveDuration);
        Assert.Equal(frozenAt, result.EndedAtUtc);          // 结束时间取按「结束」那一刻
        Assert.True(result.EndedAtUtc > startedAt);
        Assert.Single(result.Intervals);
    }

    [Fact]
    public void Unfreeze_ResumesCountingWithoutCountingFrozenWindow()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        timer.Freeze();
        time.Advance(TimeSpan.FromMinutes(5));   // 点了「继续专注」之前的空档
        timer.Unfreeze();
        time.Advance(TimeSpan.FromMinutes(7));

        Assert.False(timer.IsFrozen);
        Assert.Equal(TimeSpan.FromMinutes(17), timer.EffectiveDuration);

        var result = timer.Stop();
        Assert.Equal(TimeSpan.FromMinutes(17), result.EffectiveDuration);
        Assert.Equal(2, result.Intervals.Count);   // 冻结把它切成前后两段
    }

    [Fact]
    public void Freeze_ThenUnfreeze_ProducesContiguousIntervals()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(10));
        var firstEnd = time.UtcNow;
        timer.Freeze();
        time.Advance(TimeSpan.FromMinutes(5));
        timer.Unfreeze();
        var secondStart = time.UtcNow;
        time.Advance(TimeSpan.FromMinutes(7));
        var result = timer.Stop();

        Assert.Equal(firstEnd, result.Intervals[0].EndedAtUtc);
        Assert.Equal(secondStart, result.Intervals[1].StartedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Intervals[0].Duration);
        Assert.Equal(TimeSpan.FromMinutes(7), result.Intervals[1].Duration);
    }

    [Fact]
    public void Freeze_WhenNotRunning_DoesNothing()
    {
        var time = new FakeTimeSource();
        var timer = new FocusTimerService(time);

        timer.Freeze();          // 还没开始，什么也不做
        Assert.False(timer.IsFrozen);

        timer.Start();
        time.Advance(TimeSpan.FromMinutes(5));
        timer.Pause("喝水");
        timer.Freeze();          // 暂停中冻结也没意义
        Assert.False(timer.IsFrozen);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.EffectiveDuration);
    }
}
