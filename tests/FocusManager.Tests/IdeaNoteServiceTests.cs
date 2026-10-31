using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 随手记的读写与状态流转。
/// </summary>
public sealed class IdeaNoteServiceTests
{
    private static (TempDatabase Db, FakeTimeSource Time, IdeaNoteService Ideas, long ProjectId) CreateContext()
    {
        var db = new TempDatabase();
        var time = new FakeTimeSource();
        var project = new ProjectService(db.Service, time).Create("微观经济学——消费者理论");
        var ideas = new IdeaNoteService(db.Service, time);
        return (db, time, ideas, project.Id);
    }

    [Fact]
    public void Add_StoresNoteAsPending()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var note = ideas.Add(projectId, "  研究消费者剩余和补偿变差的联系  ");

        Assert.True(note.Id > 0);
        Assert.Equal(projectId, note.ProjectId);
        Assert.Null(note.SessionId);
        Assert.Equal("研究消费者剩余和补偿变差的联系", note.Content);
        Assert.Equal(IdeaNoteStatus.Pending, note.Status);
        Assert.Equal(time.UtcNow, note.CreatedAtUtc);
        Assert.Null(note.CompletedAtUtc);

        var reloaded = ideas.FindById(note.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("研究消费者剩余和补偿变差的联系", reloaded!.Content);
        Assert.Equal(IdeaNoteStatus.Pending, reloaded.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Add_RejectsEmptyContent(string content)
    {
        var (db, _, ideas, projectId) = CreateContext();
        using var _ = db;

        var error = Assert.Throws<ArgumentException>(() => ideas.Add(projectId, content));
        Assert.Contains("想法内容不能为空", error.Message);
    }

    [Fact]
    public void Add_RejectsTooLongContent()
    {
        var (db, _, ideas, projectId) = CreateContext();
        using var _ = db;

        var tooLong = new string('字', IdeaNoteService.MaxContentLength + 1);
        var error = Assert.Throws<ArgumentException>(() => ideas.Add(projectId, tooLong));
        Assert.Contains("最多", error.Message);

        // 刚好到上限应当可以存下。
        var exactly = new string('字', IdeaNoteService.MaxContentLength);
        var note = ideas.Add(projectId, exactly);
        Assert.Equal(IdeaNoteService.MaxContentLength, note.Content.Length);
    }

    [Fact]
    public void Add_RejectsUnknownProject()
    {
        var (db, _, ideas, _) = CreateContext();
        using var __ = db;

        var error = Assert.Throws<InvalidOperationException>(() => ideas.Add(9999, "写点东西"));
        Assert.Contains("找不到编号为 9999 的项目", error.Message);
    }

    [Fact]
    public void Add_RejectsSessionThatBelongsToAnotherProject()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var projects = new ProjectService(db.Service, time);
        var otherProject = projects.Create("另一个项目");
        var sessions = new SessionService(db.Service, time);
        var otherSession = sessions.BeginSession(otherProject.Id);

        var error = Assert.Throws<InvalidOperationException>(
            () => ideas.Add(projectId, "不该能关联过去", otherSession));
        Assert.Contains("不属于这个项目", error.Message);
    }

    [Fact]
    public void Add_CanAttachToCurrentSession()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var sessions = new SessionService(db.Service, time);
        var session = sessions.BeginSession(projectId);

        var note = ideas.Add(projectId, "整理高鸿业与范里安的理论差异", session);

        Assert.Equal(session, note.SessionId);
        Assert.Equal(1, ideas.CountBySession(session));
    }

    [Fact]
    public void ListByProject_ReturnsNewestFirst()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        ideas.Add(projectId, "第一条");
        time.Advance(TimeSpan.FromMinutes(1));
        ideas.Add(projectId, "第二条");
        time.Advance(TimeSpan.FromMinutes(1));
        ideas.Add(projectId, "第三条");

        var list = ideas.ListByProject(projectId);

        Assert.Equal(3, list.Count);
        Assert.Equal("第三条", list[0].Content);
        Assert.Equal("第一条", list[2].Content);
    }

    [Fact]
    public void ListByProject_CanFilterByStatus()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var pending = ideas.Add(projectId, "还没做的");
        var done = ideas.Add(projectId, "已经做掉的");
        ideas.SetStatus(done.Id, IdeaNoteStatus.Done);

        var pendingOnly = ideas.ListByProject(projectId, IdeaNoteStatus.Pending);
        var doneOnly = ideas.ListByProject(projectId, IdeaNoteStatus.Done);

        Assert.Single(pendingOnly);
        Assert.Equal(pending.Id, pendingOnly[0].Id);
        Assert.Single(doneOnly);
        Assert.Equal(done.Id, doneOnly[0].Id);
        Assert.Equal(2, ideas.ListByProject(projectId).Count);
    }

    [Fact]
    public void ListByProject_DoesNotMixProjects()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var other = new ProjectService(db.Service, time).Create("另一个项目");
        ideas.Add(projectId, "属于第一个项目");
        ideas.Add(other.Id, "属于第二个项目");

        Assert.Single(ideas.ListByProject(projectId));
        Assert.Equal("属于第一个项目", ideas.ListByProject(projectId)[0].Content);
        Assert.Single(ideas.ListByProject(other.Id));
    }

    [Fact]
    public void CountByProject_CountsAllAndPendingSeparately()
    {
        var (db, _, ideas, projectId) = CreateContext();
        using var _ = db;

        Assert.Equal(0, ideas.CountByProject(projectId));

        var a = ideas.Add(projectId, "A");
        ideas.Add(projectId, "B");
        ideas.SetStatus(a.Id, IdeaNoteStatus.Done);

        Assert.Equal(2, ideas.CountByProject(projectId));
        Assert.Equal(1, ideas.CountByProject(projectId, IdeaNoteStatus.Pending));
        Assert.Equal(1, ideas.CountByProject(projectId, IdeaNoteStatus.Done));
    }

    [Fact]
    public void SetStatus_DoneRecordsCompletedTime()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var note = ideas.Add(projectId, "查一下补偿变差");
        time.Advance(TimeSpan.FromMinutes(30));

        var done = ideas.SetStatus(note.Id, IdeaNoteStatus.Done);

        Assert.Equal(IdeaNoteStatus.Done, done.Status);
        Assert.Equal(time.UtcNow, done.CompletedAtUtc);
    }

    [Fact]
    public void SetStatus_BackToPendingClearsCompletedTime()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var note = ideas.Add(projectId, "查一下补偿变差");
        time.Advance(TimeSpan.FromMinutes(5));
        ideas.SetStatus(note.Id, IdeaNoteStatus.Done);

        var back = ideas.SetStatus(note.Id, IdeaNoteStatus.Pending);

        Assert.Equal(IdeaNoteStatus.Pending, back.Status);
        Assert.Null(back.CompletedAtUtc);
    }

    [Fact]
    public void SetStatus_ThrowsForUnknownNote()
    {
        var (db, _, ideas, _) = CreateContext();
        using var __ = db;

        var error = Assert.Throws<InvalidOperationException>(
            () => ideas.SetStatus(4321, IdeaNoteStatus.Done));
        Assert.Contains("找不到编号为 4321 的想法", error.Message);
    }

    [Fact]
    public void ToggleStatus_SwitchesBothWays()
    {
        var (db, _, ideas, projectId) = CreateContext();
        using var _ = db;

        var note = ideas.Add(projectId, "来回切一下");

        Assert.Equal(IdeaNoteStatus.Done, ideas.ToggleStatus(note.Id).Status);
        Assert.Equal(IdeaNoteStatus.Pending, ideas.ToggleStatus(note.Id).Status);
    }

    [Fact]
    public void Delete_RemovesNote()
    {
        var (db, _, ideas, projectId) = CreateContext();
        using var _ = db;

        var note = ideas.Add(projectId, "这条会被删掉");
        ideas.Delete(note.Id);

        Assert.Null(ideas.FindById(note.Id));
        Assert.Equal(0, ideas.CountByProject(projectId));
    }

    [Fact]
    public void Delete_ThrowsForUnknownNote()
    {
        var (db, _, ideas, _) = CreateContext();
        using var __ = db;

        var error = Assert.Throws<InvalidOperationException>(() => ideas.Delete(777));
        Assert.Contains("找不到编号为 777 的想法", error.Message);
    }

    [Fact]
    public void Notes_AreRemovedTogetherWithProject()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var note = ideas.Add(projectId, "项目删掉之后这条也不该留着");
        var projects = new ProjectService(db.Service, time);

        projects.Delete(projectId);

        Assert.Null(ideas.FindById(note.Id));
        Assert.Equal(0, ideas.CountByProject(projectId));
    }

    [Fact]
    public void Notes_SurviveAfterSessionEnds()
    {
        var (db, time, ideas, projectId) = CreateContext();
        using var _ = db;

        var sessions = new SessionService(db.Service, time);
        var session = sessions.BeginSession(projectId);
        var note = ideas.Add(projectId, "专注中想到的事", session);

        time.Advance(TimeSpan.FromMinutes(10));
        sessions.CompleteSession("读完了第三章");

        // 会话结束、甚至项目里再也没有会话，想法都应该还在。
        var reloaded = ideas.FindById(note.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("专注中想到的事", reloaded!.Content);
        Assert.Equal(session, reloaded.SessionId);
        Assert.Equal(1, ideas.CountBySession(session));
    }
}
