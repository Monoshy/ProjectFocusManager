using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 删除单条专注记录、以及作废当前这一轮。
/// </summary>
public sealed class SessionDeletionTests
{
    [Fact]
    public void DeleteSession_RemovesOnlyThatRecordAndUpdatesProjectTotal()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("线性代数");

        var firstId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(20));
        sessions.CompleteSession("第一章");

        var secondId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(30));
        sessions.CompleteSession("第二章");

        Assert.Equal(50 * 60, projects.GetTotalEffectiveSeconds(project.Id));

        var impact = sessions.DeleteSession(firstId);

        Assert.Equal(firstId, impact.SessionId);
        Assert.Equal(20 * 60, impact.EffectiveSeconds);
        Assert.Null(sessions.GetSession(firstId));
        Assert.NotNull(sessions.GetSession(secondId));
        Assert.Equal(30 * 60, projects.GetTotalEffectiveSeconds(project.Id));
        Assert.Equal(30 * 60, projects.FindById(project.Id)!.TotalEffectiveSeconds);
    }

    [Fact]
    public void DeleteSession_AlsoRemovesItsIntervalsAndPauses()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");

        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(10));
        sessions.PauseSession("接电话");
        time.Advance(TimeSpan.FromMinutes(3));
        sessions.ResumeSession();
        time.Advance(TimeSpan.FromMinutes(12));
        sessions.CompleteSession("做完习题");

        Assert.Equal(2, sessions.GetIntervals(sessionId).Count);
        Assert.Single(sessions.GetPauses(sessionId));

        sessions.DeleteSession(sessionId);

        Assert.Empty(sessions.GetIntervals(sessionId));
        Assert.Empty(sessions.GetPauses(sessionId));
        Assert.Empty(sessions.GetSessionApplications(sessionId));
    }

    [Fact]
    public void DeleteSession_KeepsIdeaNotesButDetachesThem()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);
        var ideas = new IdeaNoteService(db.Service, time);

        var project = projects.Create("项目");

        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(15));
        ideas.Add(project.Id, "记得查一下这个公式", sessionId);
        sessions.CompleteSession("看完了");

        var impact = sessions.DeleteSession(sessionId);

        Assert.Equal(1, impact.IdeaNoteCount);

        var remaining = ideas.ListByProject(project.Id);
        var note = Assert.Single(remaining);
        Assert.Null(note.SessionId);
    }

    [Fact]
    public void DeleteSession_OnRunningSession_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Throws<InvalidOperationException>(() => sessions.DeleteSession(sessionId));

        // 抛异常之后这一轮还在正常计时，不能顺手把它弄坏。
        Assert.True(sessions.HasActiveSession);
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(10 * 60, sessions.CompleteSession("做完了").EffectiveSeconds);
    }

    [Fact]
    public void DeleteSession_UnknownId_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var sessions = new SessionService(db.Service, time);

        Assert.Throws<InvalidOperationException>(() => sessions.DeleteSession(4242));
    }

    [Fact]
    public void DiscardActiveSession_LeavesNoTraceAndResetsProjectTotal()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");

        var keptId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(40));
        sessions.CompleteSession("先做完一半");

        var discardedId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(2));

        var discardedSeconds = sessions.DiscardActiveSession();

        Assert.Equal(2 * 60, discardedSeconds);
        Assert.False(sessions.HasActiveSession);
        Assert.Null(sessions.GetSession(discardedId));
        Assert.NotNull(sessions.GetSession(keptId));
        Assert.Equal(40 * 60, projects.GetTotalEffectiveSeconds(project.Id));
        Assert.Equal(40 * 60, projects.FindById(project.Id)!.TotalEffectiveSeconds);
    }

    [Fact]
    public void DiscardActiveSession_WhilePaused_StillDiscards()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var sessionId = sessions.BeginSession(project.Id);

        time.Advance(TimeSpan.FromMinutes(3));
        sessions.PauseSession("走开一下");
        time.Advance(TimeSpan.FromMinutes(10));

        var seconds = sessions.DiscardActiveSession();

        Assert.Equal(3 * 60, seconds);
        Assert.Null(sessions.GetSession(sessionId));
        Assert.Equal(0, projects.GetTotalEffectiveSeconds(project.Id));
    }

    [Fact]
    public void DiscardActiveSession_WithoutActiveSession_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var sessions = new SessionService(db.Service, time);

        Assert.Throws<InvalidOperationException>(() => sessions.DiscardActiveSession());
    }

    [Fact]
    public void DiscardActiveSession_ThenBeginAgain_Works()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(1));
        sessions.DiscardActiveSession();

        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(30));
        var session = sessions.CompleteSession("这次认真做了");

        Assert.Equal(sessionId, session.Id);
        Assert.Equal(30 * 60, session.EffectiveSeconds);
    }

    [Fact]
    public void DiscardActiveSession_KeepsIdeasBelongingToProject()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);
        var ideas = new IdeaNoteService(db.Service, time);

        var project = projects.Create("项目");
        var sessionId = sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(1));
        ideas.Add(project.Id, "刚想到的一点", sessionId);

        sessions.DiscardActiveSession();

        var note = Assert.Single(ideas.ListByProject(project.Id));
        Assert.Null(note.SessionId);
    }

    [Fact]
    public void SessionStatus_StillDistinguishesUnfinished()
    {
        // 作废走的是「删掉」而不是「记成 aborted」：历史里不该留下一条 0 分钟的记录。
        Assert.True(SessionStatus.Running.IsUnfinished());
        Assert.False(SessionStatus.Aborted.IsUnfinished());
    }
}
