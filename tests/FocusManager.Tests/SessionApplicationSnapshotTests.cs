using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 每一轮专注实际用了哪些程序——记的是当时的快照，不是「项目现在的配置」。
/// 这样以后改项目的辅助程序，不会把历史记录也改掉。
/// </summary>
public sealed class SessionApplicationSnapshotTests
{
    [Fact]
    public void BeginSession_SnapshotsPrimaryAndProjectAuxiliaries()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("微观经济学");
        var primary = applications.AddFromRunningProcess("CCtalk", "CCtalk");
        var notes = applications.AddFromRunningProcess("Obsidian", "Obsidian");
        var pdf = applications.AddFromRunningProcess("PDF 阅读器", "SumatraPDF");

        mounts.Add(project.Id, notes.Id);
        mounts.Add(project.Id, pdf.Id);

        var sessionId = sessions.BeginSession(project.Id, primary.Id);

        var snapshot = sessions.GetSessionApplications(sessionId);

        Assert.Equal(3, snapshot.Count);

        var primaryRow = Assert.Single(snapshot, a => a.IsPrimary);
        Assert.Equal(primary.Id, primaryRow.ApplicationId);
        Assert.Equal("CCtalk", primaryRow.ProcessNameSnapshot);

        Assert.Equal(
            ["Obsidian", "PDF 阅读器"],
            snapshot.Where(a => !a.IsPrimary).Select(a => a.NameSnapshot));
    }

    [Fact]
    public void BeginSession_WithoutProjectAuxiliaries_OnlySnapshotsPrimary()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var primary = applications.AddFromRunningProcess("CCtalk", "CCtalk");

        var sessionId = sessions.BeginSession(project.Id, primary.Id);

        var snapshot = sessions.GetSessionApplications(sessionId);

        var row = Assert.Single(snapshot);
        Assert.True(row.IsPrimary);
    }

    [Fact]
    public void BeginSession_PrimaryAlsoMountedAsAuxiliary_IsNotDuplicated()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var primary = applications.AddFromRunningProcess("CCtalk", "CCtalk");
        mounts.Add(project.Id, primary.Id);

        var sessionId = sessions.BeginSession(project.Id, primary.Id);

        var row = Assert.Single(sessions.GetSessionApplications(sessionId));
        Assert.True(row.IsPrimary);
    }

    [Fact]
    public void AttachAuxiliaryApplication_AddsToSessionAndToProject()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var primary = applications.AddFromRunningProcess("CCtalk", "CCtalk");
        var latecomer = applications.AddFromRunningProcess("百度网盘", "BaiduNetdisk");

        var sessionId = sessions.BeginSession(project.Id, primary.Id);
        sessions.AttachAuxiliaryApplication(latecomer.Id);

        var snapshot = sessions.GetSessionApplications(sessionId);
        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, a => a.ApplicationId == latecomer.Id && !a.IsPrimary);

        // 项目上也挂上了：下一轮自动带上。
        Assert.Equal([latecomer.Id], mounts.ListIdsForProject(project.Id));
    }

    [Fact]
    public void AttachAuxiliaryApplication_Twice_DoesNotDuplicate()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var primary = applications.AddFromRunningProcess("CCtalk", "CCtalk");
        var extra = applications.AddFromRunningProcess("Anki", "anki");

        var sessionId = sessions.BeginSession(project.Id, primary.Id);
        sessions.AttachAuxiliaryApplication(extra.Id);
        sessions.AttachAuxiliaryApplication(extra.Id);

        Assert.Equal(2, sessions.GetSessionApplications(sessionId).Count);
    }

    [Fact]
    public void AttachAuxiliaryApplication_WithoutActiveSession_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var applications = new ApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var app = applications.AddFromRunningProcess("Anki", "anki");

        Assert.Throws<InvalidOperationException>(() => sessions.AttachAuxiliaryApplication(app.Id));
    }

    [Fact]
    public void Snapshot_SurvivesLaterProjectChanges()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);
        var sessions = new SessionService(db.Service, time);

        var project = projects.Create("项目");
        var primary = applications.AddFromRunningProcess("CCtalk", "CCtalk");
        var notes = applications.AddFromRunningProcess("Obsidian", "Obsidian");

        mounts.Add(project.Id, notes.Id);

        var sessionId = sessions.BeginSession(project.Id, primary.Id);
        time.Advance(TimeSpan.FromMinutes(30));
        sessions.CompleteSession("看完了第一讲");

        // 结束后把辅助程序从项目上摘掉，历史快照不该跟着变。
        mounts.Remove(project.Id, notes.Id);

        var snapshot = sessions.GetSessionApplications(sessionId);
        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, a => a.NameSnapshot == "Obsidian");
    }
}
