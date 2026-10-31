using FocusManager.Core.Services;
using FocusManager.Core.Time;
using FocusManager.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace FocusManager.Tests;

/// <summary>
/// 删除项目的测试。
///
/// 删除是这个程序里唯一会真正丢数据的操作，所以测得比较细：
/// 该删的都删干净、不该动的不能动、还有专注没结束时必须拒绝。
/// </summary>
public sealed class ProjectDeletionTests
{
    [Fact]
    public void GetDeletionImpact_CountsSessionsAndTotalTime()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("微观经济学——消费者理论");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(30));
        sessions.CompleteSession("看完消费者剩余");

        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(15));
        sessions.CompleteSession("做了 5 道题");

        var impact = projects.GetDeletionImpact(project.Id);

        Assert.Equal(project.Id, impact.ProjectId);
        Assert.Equal("微观经济学——消费者理论", impact.ProjectName);
        Assert.Equal(2, impact.SessionCount);
        Assert.Equal((30 + 15) * 60, impact.TotalEffectiveSeconds);
        Assert.Equal("45 分钟", impact.TotalEffectiveText);
        Assert.True(impact.HasRecords);
    }

    [Fact]
    public void Delete_RemovesProjectAndEverythingUnderIt()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var doomed = projects.Create("要删掉的项目");

        sessions.BeginSession(doomed.Id);
        time.Advance(TimeSpan.FromMinutes(10));
        sessions.PauseSession("喝水");
        time.Advance(TimeSpan.FromMinutes(2));
        sessions.ResumeSession();
        time.Advance(TimeSpan.FromMinutes(5));
        sessions.CompleteSession("写了半页笔记", "下次继续");

        InsertIdeaNote(db, doomed.Id, "研究一下补偿变差");

        var impact = projects.Delete(doomed.Id);

        Assert.Equal(1, impact.SessionCount);
        Assert.Null(projects.FindById(doomed.Id));

        // 子表必须一条不剩，否则数据库里会留下找不到主人的孤儿记录
        Assert.Equal(0, CountRows(db, "SELECT COUNT(*) FROM FocusSessions WHERE ProjectId = " + doomed.Id));
        Assert.Equal(0, CountRows(db, "SELECT COUNT(*) FROM FocusIntervals"));
        Assert.Equal(0, CountRows(db, "SELECT COUNT(*) FROM PauseRecords"));
        Assert.Equal(0, CountRows(db, "SELECT COUNT(*) FROM IdeaNotes WHERE ProjectId = " + doomed.Id));
    }

    [Fact]
    public void Delete_LeavesOtherProjectsAlone()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var keep = projects.Create("保留的项目");
        var doomed = projects.Create("删掉的项目");

        sessions.BeginSession(keep.Id);
        time.Advance(TimeSpan.FromMinutes(20));
        sessions.CompleteSession("保留的记录");

        sessions.BeginSession(doomed.Id);
        time.Advance(TimeSpan.FromMinutes(5));
        sessions.CompleteSession("要删的记录");

        projects.Delete(doomed.Id);

        var survivor = projects.FindById(keep.Id);
        Assert.NotNull(survivor);
        Assert.Equal(20 * 60, survivor!.TotalEffectiveSeconds);
        Assert.Equal(20 * 60, projects.GetTotalEffectiveSeconds(keep.Id));

        // 保留项目的那一轮记录必须还在
        var keepSessions = new StatisticsService(db.Service).GetSessions(keep.Id);
        Assert.Single(keepSessions);
        Assert.Equal(20 * 60, keepSessions[0].EffectiveSeconds);
    }

    [Fact]
    public void Delete_RefusesWhenSessionIsStillRunning()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("正在专注的项目");
        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(3));

        var error = Assert.Throws<InvalidOperationException>(() => projects.Delete(project.Id));
        Assert.Contains("还有一轮专注没有结束", error.Message);

        // 拒绝之后什么都不该被删掉
        Assert.NotNull(projects.FindById(project.Id));
        Assert.Single(sessions.GetUnfinishedSessions());

        sessions.AbortSession();
    }

    [Fact]
    public void Delete_RefusesWhenSessionIsPaused()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("暂停中的项目");
        sessions.BeginSession(project.Id);
        time.Advance(TimeSpan.FromMinutes(4));
        sessions.PauseSession("临时有事");

        Assert.Throws<InvalidOperationException>(() => projects.Delete(project.Id));
        Assert.NotNull(projects.FindById(project.Id));

        sessions.AbortSession();
    }

    [Fact]
    public void Delete_ThrowsForUnknownProject()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        Assert.Throws<InvalidOperationException>(() => projects.Delete(999));
        Assert.Throws<InvalidOperationException>(() => projects.GetDeletionImpact(999));
    }

    [Fact]
    public void Delete_WorksForProjectWithoutAnyRecords()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service, new FakeTimeSource());

        var empty = projects.Create("刚建好就后悔了");
        var impact = projects.Delete(empty.Id);

        Assert.Equal(0, impact.SessionCount);
        Assert.False(impact.HasRecords);
        Assert.Empty(projects.List());
    }

    private static void InsertIdeaNote(TempDatabase db, long projectId, string content)
    {
        using var connection = db.Service.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO IdeaNotes (ProjectId, SessionId, Content, Status, CreatedAtUtc)
            VALUES ($projectId, NULL, $content, 'pending', $createdAt);
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$createdAt", "2026-01-01T09:00:00.0000000Z");
        command.ExecuteNonQuery();
    }

    private static int CountRows(TempDatabase db, string sql)
    {
        using var connection = db.Service.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
