using FocusManager.Core.Services;

namespace FocusManager.Tests;

public class FocusAllowListTests
{
    [Fact]
    public void PrimaryApplication_IsAllowed()
    {
        var list = new FocusAllowList("CCtalk");

        Assert.True(list.IsAllowed("CCtalk"));
    }

    [Fact]
    public void Obsidian_IsAlwaysAllowed()
    {
        var list = new FocusAllowList("CCtalk");

        Assert.True(list.IsAllowed("Obsidian"));
    }

    [Fact]
    public void OwnProcess_IsAlwaysAllowed()
    {
        var list = new FocusAllowList("CCtalk");

        Assert.True(list.IsAllowed("ProjectFocusManager"));
    }

    [Fact]
    public void OtherRegisteredApplication_DoesNotGetPermissionAutomatically()
    {
        // 本轮选的是 CCtalk，百度网盘即使已经登记在应用管理里，也不算允许。
        var list = new FocusAllowList("CCtalk");

        Assert.False(list.IsAllowed("BaiduNetdisk"));
    }

    [Fact]
    public void NecessarySystemWindows_AreAllowed()
    {
        var list = new FocusAllowList("CCtalk");

        Assert.True(list.IsAllowed("explorer"));
        Assert.True(list.IsAllowed("ShellExperienceHost"));
        Assert.True(list.IsAllowed("TextInputHost"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownProcess_IsNotAllowed(string? processName)
    {
        var list = new FocusAllowList("CCtalk");

        Assert.False(list.IsAllowed(processName));
    }

    [Theory]
    [InlineData("cctalk")]
    [InlineData("CCtalk.exe")]
    [InlineData("  CCtalk  ")]
    [InlineData("obsidian")]
    [InlineData("Explorer.EXE")]
    public void Comparison_IgnoresCaseWhitespaceAndExeSuffix(string processName)
    {
        var list = new FocusAllowList("CCtalk");

        Assert.True(list.IsAllowed(processName));
    }

    [Fact]
    public void PrimaryProcessName_IsNormalized()
    {
        var list = new FocusAllowList("  BaiduNetdisk.exe ");

        Assert.Equal("BaiduNetdisk", list.PrimaryProcessName);
    }

    [Fact]
    public void IsSystemProcess_RecognizesShellWindows()
    {
        Assert.True(FocusAllowList.IsSystemProcess("explorer"));
        Assert.False(FocusAllowList.IsSystemProcess("CCtalk"));
        Assert.False(FocusAllowList.IsSystemProcess(null));
    }

    [Fact]
    public void ExtraAllowedProcessNames_AreHonoured()
    {
        var list = new FocusAllowList("CCtalk", ["WeChat"]);

        Assert.True(list.IsAllowed("WeChat"));
    }
}
