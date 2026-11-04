using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 项目 ↔ 辅助程序 的挂载关系。
/// 需求：一个项目可以同时挂多个辅助程序，项目进行中也能加。
/// </summary>
public sealed class ProjectApplicationServiceTests
{
    [Fact]
    public void Add_ThenList_ReturnsApplicationsInAddOrder()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);

        var project = projects.Create("毕业论文");
        var notion = applications.AddFromRunningProcess("Notion", "Notion");
        var zotero = applications.AddFromRunningProcess("Zotero", "Zotero");
        var word = applications.AddFromRunningProcess("Word", "WINWORD");

        Assert.True(mounts.Add(project.Id, notion.Id));
        Assert.True(mounts.Add(project.Id, zotero.Id));
        Assert.True(mounts.Add(project.Id, word.Id));

        var list = mounts.ListForProject(project.Id);

        Assert.Equal(3, list.Count);
        Assert.Equal(["Notion", "Zotero", "Word"], list.Select(a => a.Name));
        Assert.Equal(3, mounts.CountForProject(project.Id));
    }

    [Fact]
    public void Add_SameApplicationTwice_IsIdempotent()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);

        var project = projects.Create("英语听力");
        var app = applications.AddFromRunningProcess("PotPlayer", "PotPlayerMini64");

        Assert.True(mounts.Add(project.Id, app.Id));
        Assert.False(mounts.Add(project.Id, app.Id));
        Assert.Single(mounts.ListForProject(project.Id));
    }

    [Fact]
    public void Remove_DetachesOnlyThatProject()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);

        var first = projects.Create("项目一");
        var second = projects.Create("项目二");
        var app = applications.AddFromRunningProcess("Anki", "anki");

        mounts.Add(first.Id, app.Id);
        mounts.Add(second.Id, app.Id);

        Assert.True(mounts.Remove(first.Id, app.Id));

        Assert.Empty(mounts.ListForProject(first.Id));
        Assert.Single(mounts.ListForProject(second.Id));
        Assert.False(mounts.Remove(first.Id, app.Id));
    }

    [Fact]
    public void Add_UnknownProjectOrApplication_Throws()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);

        var project = projects.Create("项目");
        var app = applications.AddFromRunningProcess("Anki", "anki");

        Assert.Throws<InvalidOperationException>(() => mounts.Add(9999, app.Id));
        Assert.Throws<InvalidOperationException>(() => mounts.Add(project.Id, 9999));
    }

    [Fact]
    public void ListProcessNamesForProject_ReturnsProcessNames()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);

        var project = projects.Create("网课");
        var a = applications.AddFromRunningProcess("百度网盘", "BaiduNetdisk");
        var b = applications.AddFromRunningProcess("CCtalk", "CCtalk");

        mounts.Add(project.Id, a.Id);
        mounts.Add(project.Id, b.Id);

        Assert.Equal(["BaiduNetdisk", "CCtalk"], mounts.ListProcessNamesForProject(project.Id));
    }

    [Fact]
    public void DeletingApplication_AlsoRemovesItsMounts()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);
        var applications = new ApplicationService(db.Service, time);
        var mounts = new ProjectApplicationService(db.Service, time);

        var project = projects.Create("项目");
        var app = applications.AddFromRunningProcess("Anki", "anki");
        mounts.Add(project.Id, app.Id);

        applications.Delete(app.Id);

        Assert.Empty(mounts.ListForProject(project.Id));
    }
}
