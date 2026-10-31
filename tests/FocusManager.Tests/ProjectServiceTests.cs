using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>项目管理的测试：新建、改名、归档、搜索、累计时间。</summary>
public sealed class ProjectServiceTests
{
    [Fact]
    public void Create_SavesProject()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);

        var project = projects.Create("微观经济学——消费者理论", "第 3 章");

        Assert.True(project.Id > 0);
        Assert.Equal("微观经济学——消费者理论", project.Name);
        Assert.Equal("第 3 章", project.Description);
        Assert.False(project.IsArchived);
        Assert.Equal(0, project.TotalEffectiveSeconds);
    }

    [Fact]
    public void Create_RejectsEmptyName()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        Assert.Throws<ArgumentException>(() => projects.Create(""));
        Assert.Throws<ArgumentException>(() => projects.Create("   "));
    }

    [Fact]
    public void Create_RejectsTooLongName()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        var tooLong = new string('学', 201);
        Assert.Throws<ArgumentException>(() => projects.Create(tooLong));
    }

    [Fact]
    public void List_ReturnsNewestFirst()
    {
        using var db = new TempDatabase();
        var time = new FakeTimeSource();
        var projects = new ProjectService(db.Service, time);

        var first = projects.Create("项目一");
        time.Advance(TimeSpan.FromMinutes(1));
        var second = projects.Create("项目二");

        var list = projects.List();

        Assert.Equal(2, list.Count);
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
    }

    [Fact]
    public void List_HidesArchivedByDefault()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        var active = projects.Create("在学的");
        var archived = projects.Create("学完的");
        projects.SetArchived(archived.Id, true);

        var visible = projects.List();
        var all = projects.List(includeArchived: true);

        Assert.Single(visible);
        Assert.Equal(active.Id, visible[0].Id);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void FindById_ReturnsNullForUnknownId()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        Assert.Null(projects.FindById(9999));
    }

    [Fact]
    public void Rename_ChangesName()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        var project = projects.Create("旧名字");
        projects.Rename(project.Id, "新名字");

        Assert.Equal("新名字", projects.FindById(project.Id)!.Name);
    }

    [Fact]
    public void Rename_UnknownId_Throws()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        Assert.Throws<InvalidOperationException>(() => projects.Rename(12345, "随便"));
    }

    [Fact]
    public void Search_FindsByKeyword()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        projects.Create("微观经济学");
        projects.Create("高等数学");
        projects.Create("英语听力");

        var hits = projects.Search("经济");

        Assert.Single(hits);
        Assert.Equal("微观经济学", hits[0].Name);
    }

    [Fact]
    public void Search_EmptyKeyword_ReturnsEmpty()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        projects.Create("微观经济学");

        Assert.Empty(projects.Search(""));
        Assert.Empty(projects.Search("   "));
    }

    [Fact]
    public void Search_TreatsWildcardCharactersAsPlainText()
    {
        using var db = new TempDatabase();
        var projects = new ProjectService(db.Service);

        projects.Create("微观经济学");
        projects.Create("概率论 100% 掌握");

        // 用户搜 "100%"，不该因为 % 是通配符就匹配到所有项目。
        var hits = projects.Search("100%");

        Assert.Single(hits);
        Assert.Equal("概率论 100% 掌握", hits[0].Name);
    }
}
