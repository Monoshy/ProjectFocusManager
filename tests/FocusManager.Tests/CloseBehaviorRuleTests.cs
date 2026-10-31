using FocusManager.Core.Services;

namespace FocusManager.Tests;

public class CloseBehaviorRuleTests
{
    [Fact]
    public void Default_IsAsk()
        => Assert.Equal(CloseBehavior.Ask, CloseBehaviorRule.Default);

    [Theory]
    [InlineData("Ask", CloseBehavior.Ask)]
    [InlineData("Tray", CloseBehavior.MinimizeToTray)]
    [InlineData("Exit", CloseBehavior.Exit)]
    public void Resolve_ReadsKnownValues(string raw, CloseBehavior expected)
        => Assert.Equal(expected, CloseBehaviorRule.Resolve(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tray")]
    [InlineData("什么都不是")]
    public void Resolve_FallsBackToAskForUnknownValues(string? raw)
        => Assert.Equal(CloseBehavior.Ask, CloseBehaviorRule.Resolve(raw));

    [Theory]
    [InlineData(CloseBehavior.Ask, "Ask")]
    [InlineData(CloseBehavior.MinimizeToTray, "Tray")]
    [InlineData(CloseBehavior.Exit, "Exit")]
    public void ToSettingValue_RoundTrips(CloseBehavior behavior, string expected)
    {
        Assert.Equal(expected, CloseBehaviorRule.ToSettingValue(behavior));
        Assert.Equal(behavior, CloseBehaviorRule.Resolve(expected));
    }

    [Fact]
    public void ToDisplayName_IsChineseForEveryValue()
    {
        Assert.Equal("每次询问", CloseBehaviorRule.ToDisplayName(CloseBehavior.Ask));
        Assert.Equal("最小化到系统托盘", CloseBehaviorRule.ToDisplayName(CloseBehavior.MinimizeToTray));
        Assert.Equal("直接退出程序", CloseBehaviorRule.ToDisplayName(CloseBehavior.Exit));
    }

    [Fact]
    public void AllowedValues_MatchSettingValues()
    {
        foreach (var behavior in Enum.GetValues<CloseBehavior>())
            Assert.Contains(CloseBehaviorRule.ToSettingValue(behavior), CloseBehaviorRule.AllowedValues);
    }
}
