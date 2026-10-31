using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 会话全流程测试：开始 → 暂停 → 恢复 → 结束，以及程序异常退出后的恢复。
/// 这些是「计时测试」和「数据安全测试」里最关键的部分。
/// </summary>
public sealed class SessionServiceTests
{
    [Fact]
    public void FullSession_PersistsIntervalsPausesAndTotals()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("微观经济学——消费者理论");

        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(25));      // 第一段
        sessions.PauseSession("喝水");
        time.Advance(TimeSpan.FromMinutes(5));       // 暂停
        sessions.ResumeSession();
        time.Advance(TimeSpan.FromMinutes(35));      // 第二段

        var session = sessions.CompleteSession("看完消费者理论第 3 节", "补偿变差还不熟");

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(60 * 60, session.EffectiveSeconds);
        Assert.Equal(5 * 60, session.PausedSeconds);
        Assert.Equal(1, session.PauseCount);
        Assert.Equal("看完消费者理论第 3 节", session.ProgressNote);
        Assert.Equal("补偿变差还不熟", session.ReflectionNote);
        Assert.NotNull(session.EndedAtUtc);

        var intervals = sessions.GetIntervals(sessionId);
        Assert.Equal(2, intervals.Count);
        Assert.Equal(25 * 60, intervals[0].DurationSeconds);
        Assert.Equal(35 * 60, intervals[1].DurationSeconds);

        var pauses = sessions.GetPauses(sessionId);
        var pause = Assert.Single(pauses);
        Assert.Equal("喝水", pause.Reason);
        Assert.Equal(5 * 60, pause.DurationSeconds);

        // 项目累计时间必须同步更新
        Assert.Equal(60 * 60, projects.GetTotalEffectiveSeconds(project.Id));
        Assert.Equal(60 * 60, projects.FindById(project.Id)!.TotalEffectiveSeconds);
    }

    [Fact]
    public void CompleteSession_RequiresProgressNote()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(10));

        Assert.Throws<ArgumentException>(() => sessions.CompleteSession(""));
        Assert.Throws<ArgumentException>(() => sessions.CompleteSession("   "));

        // 报错之后会话还应该在，可以继续正常结束
        Assert.True(sessions.HasActiveSession);
        var session = sessions.CompleteSession("补上总结");
        Assert.Equal(10 * 60, session.EffectiveSeconds);
    }

    [Fact]
    public void AbortSession_KeepsEffectiveTime()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(12));

        var session = sessions.AbortSession("临时有事");

        Assert.Equal(SessionStatus.Aborted, session.Status);
        Assert.Equal(12 * 60, session.EffectiveSeconds);
        Assert.False(sessions.HasActiveSession);
    }

    [Fact]
    public void EndSession_ClearsActiveState()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        sessions.BeginSession(project.Id);

        Assert.True(sessions.HasActiveSession);
        Assert.NotNull(sessions.CurrentSessionId);

        time.Advance(TimeSpan.FromMinutes(1));
        sessions.CompleteSession("做完了");

        Assert.False(sessions.HasActiveSession);
        Assert.Null(sessions.CurrentSessionId);
        Assert.Null(sessions.Timer);
    }

    [Fact]
    public void BeginSession_Twice_Throws()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);
        var sessions = new SessionService(db.Service);

        var project = projects.Create("测试项目");
        sessions.BeginSession(project.Id);

        Assert.Throws<InvalidOperationException>(() => sessions.BeginSession(project.Id));
    }

    [Fact]
    public void BeginSession_UnknownProject_Throws()
    {
        using var db = new TempDatabase();
        var sessions = new SessionService(db.Service);

        Assert.Throws<InvalidOperationException>(() => sessions.BeginSession(9999));
    }

    [Fact]
    public void BeginSession_ArchivedProject_Throws()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);
        var sessions = new SessionService(db.Service);

        var project = projects.Create("学完的项目");
        projects.SetArchived(project.Id, true);

        Assert.Throws<InvalidOperationException>(() => sessions.BeginSession(project.Id));
    }

    [Fact]
    public void PauseSession_SavesReason()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(5));

        sessions.PauseSession("查找资料");

        var pauses = sessions.GetPauses(sessionId);
        var pause = Assert.Single(pauses);
        Assert.Equal("查找资料", pause.Reason);
        Assert.Null(pause.EndedAtUtc);   // 还在暂停中

        var stored = sessions.GetSession(sessionId)!;
        Assert.Equal(SessionStatus.Paused, stored.Status);
    }

    [Fact]
    public void PauseSession_Twice_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        sessions.BeginSession(project.Id);
        sessions.PauseSession("喝水");

        Assert.Throws<InvalidOperationException>(() => sessions.PauseSession("再暂停一次"));
    }

    [Fact]
    public void UnfinishedSession_IsDetectedOnNextStartup()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(15));
        sessions.SaveCheckpoint();

        // 模拟程序被强制关闭后重新打开：数据库还在，但内存状态没了。
        var reopened = new SessionService(db.Service, time);
        var unfinished = reopened.GetUnfinishedSessions();

        var only = Assert.Single(unfinished);
        Assert.Equal(sessionId, only.Id);
        Assert.True(only.Status.IsUnfinished());
    }

    [Fact]
    public void FinishUnfinishedSession_DoesNotCountDowntime()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        var sessionId = sessions.BeginSession(project.Id);

        time.Advance(TimeSpan.FromMinutes(15));
        sessions.SaveCheckpoint();                    // 心跳：把 15 分钟存下来

        time.Advance(TimeSpan.FromHours(2));          // 电脑关机 / 程序没运行的两小时

        var reopened = new SessionService(db.Service, time);
        var finished = reopened.FinishUnfinishedSession(sessionId);

        // 只有真正专注过的 15 分钟被计入，关机的两小时不算。
        Assert.Equal(15 * 60, finished.EffectiveSeconds);
        Assert.Equal(SessionStatus.Recovered, finished.Status);
        Assert.Empty(reopened.GetUnfinishedSessions());
    }

    [Fact]
    public void ResumeUnfinishedSession_ContinuesWithoutCountingDowntime()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        var sessionId = sessions.BeginSession(project.Id);

        time.Advance(TimeSpan.FromMinutes(10));
        sessions.SaveCheckpoint();

        time.Advance(TimeSpan.FromMinutes(40));       // 关机期间

        var reopened = new SessionService(db.Service, time);
        var resumed = reopened.ResumeUnfinishedSession(sessionId);
        Assert.True(reopened.HasActiveSession);

        time.Advance(TimeSpan.FromMinutes(5));        // 恢复后又专注 5 分钟
        var done = reopened.CompleteSession("接着看完");

        Assert.Equal(15 * 60, done.EffectiveSeconds);
        Assert.Equal(10 * 60 + 5 * 60, projects.GetTotalEffectiveSeconds(project.Id));
    }

    [Fact]
    public void FinishUnfinishedSession_Twice_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("测试项目");
        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(3));
        sessions.SaveCheckpoint();

        var reopened = new SessionService(db.Service, time);
        reopened.FinishUnfinishedSession(sessionId);

        Assert.Throws<InvalidOperationException>(() => reopened.FinishUnfinishedSession(sessionId));
    }

    [Fact]
    public void Statistics_SumMultipleSessionsOfSameProject()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);
        var statistics = new StatisticsService(db.Service);

        var project = projects.Create("微观经济学");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(85));
        sessions.CompleteSession("第 1 天");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(110));
        sessions.CompleteSession("第 2 天");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(65));
        sessions.CompleteSession("第 3 天");

        var stats = statistics.GetProjectStatistics(project.Id);

        Assert.Equal((85 + 110 + 65) * 60, stats.TotalEffectiveSeconds);
        Assert.Equal(3, stats.SessionCount);
        Assert.Equal("4 小时 20 分钟", Core.Time.TimeFormatter.ToChinese(stats.TotalEffectiveTime));
    }

    [Fact]
    public void CompleteSession_AfterFreeze_StoresFrozenEndTimeAndExcludesSummaryWriting()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("线性代数");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(40));

        // 模拟界面：点「结束」→ 冻结 → 用户在总结窗口里写了 5 分钟 → 保存
        var frozenAt = time.UtcNow;
        sessions.FreezeTimerForSummary();
        time.Advance(TimeSpan.FromMinutes(5));

        var session = sessions.CompleteSession("把特征值那一节看完了");

        // 写总结的 5 分钟不能算进有效专注时间
        Assert.Equal(40 * 60, session.EffectiveSeconds);
        // 数据库里的结束时间必须是「按结束」那一刻，而不是保存那一刻
        Assert.Equal(frozenAt, session.EndedAtUtc);

        // 最后一段的结束时间也要和会话结束时间一致
        var intervals = sessions.GetIntervals(session.Id);
        Assert.NotEmpty(intervals);
        Assert.Equal(frozenAt, intervals[^1].EndedAtUtc);
    }

    [Fact]
    public void CompleteSession_AfterFreezeThenUnfreeze_KeepsCounting()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("概率论");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(10));

        // 点了结束，但在总结窗口里点了「继续专注」
        sessions.FreezeTimerForSummary();
        time.Advance(TimeSpan.FromMinutes(3));
        sessions.UnfreezeTimerAfterSummary();
        time.Advance(TimeSpan.FromMinutes(10));

        var session = sessions.CompleteSession("刷了两套题");

        Assert.Equal(20 * 60, session.EffectiveSeconds);   // 10 + 10，中间的 3 分钟不算
        Assert.Equal(2, sessions.GetIntervals(session.Id).Count);
    }

    [Fact]
    public void VeryShortSession_DoesNotCreateZeroLengthInterval()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("随手试试");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMilliseconds(200));   // 刚点开始就点结束
        var session = sessions.CompleteSession("试一下");

        Assert.Equal(0, session.EffectiveSeconds);
        Assert.Empty(sessions.GetIntervals(session.Id));
    }
}
