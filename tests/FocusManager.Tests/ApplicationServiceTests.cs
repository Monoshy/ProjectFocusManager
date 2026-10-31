using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

public sealed class ApplicationServiceTests
{
    /// <summary>造一个「看起来像 exe」的假文件，用来测试路径校验。</summary>
    private static string CreateFakeExe(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);

        File.WriteAllText(path, "not a real executable, only for tests");

        return path;
    }

    [Fact]
    public void EnsureDefaults_CreatesThreeBuiltInApplications()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.EnsureDefaults();

        var applications = service.List();

        Assert.Equal(3, applications.Count);
        Assert.All(applications, a => Assert.True(a.IsBuiltIn));
        Assert.Contains(applications, a => a.Name == "Obsidian");
        Assert.Contains(applications, a => a.Name == "百度网盘");
        Assert.Contains(applications, a => a.Name == "CCtalk");
    }

    [Fact]
    public void EnsureDefaults_IsIdempotent()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.EnsureDefaults();
        service.EnsureDefaults();
        service.EnsureDefaults();

        Assert.Equal(3, service.List().Count);
    }

    [Fact]
    public void List_ExcludesDisabledApplicationsByDefault()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.EnsureDefaults();

        var obsidian = service.List().First(a => a.Name == "Obsidian");
        service.SetEnabled(obsidian.Id, false);

        Assert.DoesNotContain(service.List(), a => a.Name == "Obsidian");
        Assert.Contains(service.List(includeDisabled: true), a => a.Name == "Obsidian");
    }

    [Fact]
    public void Add_RejectsEmptyName()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);
        var exe = CreateFakeExe(db.Root, "StudyApp.exe");

        var error = Assert.Throws<ArgumentException>(() => service.Add("   ", exe));

        Assert.Contains("应用名称不能为空", error.Message);
    }

    [Fact]
    public void Add_RejectsMissingFile()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        var missing = Path.Combine(db.Root, "NotThere.exe");

        var error = Assert.Throws<ArgumentException>(() => service.Add("不存在", missing));

        Assert.Contains("找不到这个程序文件", error.Message);
    }

    [Fact]
    public void Add_RejectsDuplicatePath()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);
        var exe = CreateFakeExe(db.Root, "StudyApp.exe");

        service.Add("学习软件", exe);

        var error = Assert.Throws<InvalidOperationException>(() => service.Add("另一个名字", exe));

        Assert.Contains("已经在应用列表里", error.Message);
    }

    [Fact]
    public void Add_RecordsProcessNameFromPath()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);
        var exe = CreateFakeExe(db.Root, "StudyApp.exe");

        var added = service.Add("我的学习软件", exe);

        Assert.Equal("StudyApp", added.ProcessName);
        Assert.False(added.IsBuiltIn);
        Assert.True(added.IsEnabled);
        Assert.Equal("我的学习软件", added.Name);
    }

    [Fact]
    public void Update_RejectsMissingFile()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);
        var exe = CreateFakeExe(db.Root, "StudyApp.exe");

        var added = service.Add("学习软件", exe);

        var missing = Path.Combine(db.Root, "Gone.exe");

        var error = Assert.Throws<ArgumentException>(() => service.Update(added.Id, "学习软件", missing));

        Assert.Contains("找不到这个程序文件", error.Message);
    }

    [Fact]
    public void Update_AllowsClearingPathForBuiltInEntry()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.EnsureDefaults();

        var obsidian = service.List().First(a => a.Name == "Obsidian");

        service.Update(obsidian.Id, "Obsidian（已改名）", string.Empty);

        var updated = service.FindById(obsidian.Id);

        Assert.NotNull(updated);
        Assert.Equal("Obsidian（已改名）", updated.Name);
        Assert.Equal(string.Empty, updated.ExecutablePath);

        // 路径清空之后仍然记得「靠什么进程名识别它」——否则这个应用就再也认不出来了。
        Assert.Equal("Obsidian", updated.ProcessName);
    }

    [Fact]
    public void Delete_RemovesApplication()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);
        var exe = CreateFakeExe(db.Root, "StudyApp.exe");

        var added = service.Add("学习软件", exe);

        service.Delete(added.Id);

        Assert.Null(service.FindById(added.Id));
    }

    [Fact]
    public void GuessProcessName_ReturnsFileNameWithoutExtension()
    {
        Assert.Equal("Obsidian", ApplicationService.GuessProcessName(@"C:\Apps\Obsidian.exe"));
        Assert.Equal("CCtalk", ApplicationService.GuessProcessName(@"D:\Program Files\CCtalk\CCtalk.EXE"));
        Assert.Equal(string.Empty, ApplicationService.GuessProcessName("   "));
    }

    [Fact]
    public void HasUsablePath_DetectsMissingFile()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);
        var exe = CreateFakeExe(db.Root, "StudyApp.exe");

        var added = service.Add("学习软件", exe);

        Assert.True(ApplicationService.HasUsablePath(added));

        File.Delete(exe);

        Assert.False(ApplicationService.HasUsablePath(added));
    }

    [Fact]
    public void HasUsablePath_ReturnsFalseForBuiltInEntryWithoutPath()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.EnsureDefaults();

        var obsidian = service.List().First(a => a.Name == "Obsidian");

        Assert.False(ApplicationService.HasUsablePath(obsidian));
    }

    [Fact]
    public void Update_KeepsProcessNameWhenPathIsCleared()
    {
        // 预置条目本来就没有路径。只改名字时，不能把「靠什么识别这个程序」弄丢。
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.EnsureDefaults();

        var baidu = service.List().First(a => a.Name == "百度网盘");

        service.Update(baidu.Id, "百度网盘（学习资料）", string.Empty);

        var updated = service.FindById(baidu.Id);

        Assert.NotNull(updated);
        Assert.Equal("百度网盘（学习资料）", updated!.Name);
        Assert.Equal("BaiduNetdisk", updated.ProcessName);
    }

    [Fact]
    public void AddFromRunningProcess_WorksWithoutExecutablePath()
    {
        // 有些程序拿不到路径（受保护进程），但靠进程名就能识别窗口。
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        var added = service.AddFromRunningProcess("网易云课堂", "NetEaseClass");

        Assert.Equal("网易云课堂", added.Name);
        Assert.Equal("NetEaseClass", added.ProcessName);
        Assert.Equal(string.Empty, added.ExecutablePath);
        Assert.False(added.IsBuiltIn);
        Assert.True(added.IsEnabled);
    }

    [Fact]
    public void AddFromRunningProcess_NormalizesProcessName()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        var added = service.AddFromRunningProcess("随便一个程序", "  SomeApp.exe ");

        Assert.Equal("SomeApp", added.ProcessName);
    }

    [Fact]
    public void AddFromRunningProcess_RejectsDuplicateProcessName()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        service.AddFromRunningProcess("第一个名字", "SomeApp");

        var ex = Assert.Throws<InvalidOperationException>(
            () => service.AddFromRunningProcess("第二个名字", "someapp"));

        Assert.Equal("这个程序已经在应用列表里了。", ex.Message);
    }

    [Fact]
    public void AddFromRunningProcess_RejectsEmptyProcessName()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        Assert.Throws<ArgumentException>(() => service.AddFromRunningProcess("名字", "  "));
    }

    [Fact]
    public void AddFromRunningProcess_DropsMissingExecutablePath()
    {
        using var db = new TempDatabase();
        var service = new ApplicationService(db.Service);

        var added = service.AddFromRunningProcess("名字", "SomeApp", @"C:\不存在\SomeApp.exe");

        Assert.Equal(string.Empty, added.ExecutablePath);
    }
}
