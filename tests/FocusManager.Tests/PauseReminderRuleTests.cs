using FocusManager.Core.Services;

namespace FocusManager.Tests;

public class PauseReminderRuleTests
{
    [Fact]
    public void ResolveThreshold_FallsBackToFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), PauseReminderRule.ResolveThreshold(null));
        Assert.Equal(TimeSpan.FromMinutes(5), PauseReminderRule.ResolveThreshold(""));
        Assert.Equal(TimeSpan.FromMinutes(5), PauseReminderRule.ResolveThreshold("   "));
        Assert.Equal(TimeSpan.FromMinutes(5), PauseReminderRule.ResolveThreshold("不是数字"));
        Assert.Equal(TimeSpan.FromMinutes(5), PauseReminderRule.ResolveThreshold("-3"));
    }

    [Fact]
    public void ResolveThreshold_UsesStoredMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), PauseReminderRule.ResolveThreshold("5"));
        Assert.Equal(TimeSpan.FromMinutes(10), PauseReminderRule.ResolveThreshold("10"));
        Assert.Equal(TimeSpan.FromMinutes(15), PauseReminderRule.ResolveThreshold("15"));
    }

    [Fact]
    public void ResolveThreshold_ReturnsNullWhenTurnedOff()
    {
        Assert.Null(PauseReminderRule.ResolveThreshold("0"));
    }

    [Fact]
    public void ShouldRemind_OnlyAfterThreshold()
    {
        var threshold = TimeSpan.FromMinutes(5);

        Assert.False(PauseReminderRule.ShouldRemind(TimeSpan.FromMinutes(4.99), threshold, alreadyReminded: false));
        Assert.True(PauseReminderRule.ShouldRemind(TimeSpan.FromMinutes(5), threshold, alreadyReminded: false));
        Assert.True(PauseReminderRule.ShouldRemind(TimeSpan.FromMinutes(40), threshold, alreadyReminded: false));
    }

    [Fact]
    public void ShouldRemind_NeverTwice()
    {
        var threshold = TimeSpan.FromMinutes(5);

        Assert.False(PauseReminderRule.ShouldRemind(TimeSpan.FromMinutes(30), threshold, alreadyReminded: true));
    }

    [Fact]
    public void ShouldRemind_DoesNothingWhenTurnedOff()
    {
        Assert.False(PauseReminderRule.ShouldRemind(TimeSpan.FromHours(3), threshold: null, alreadyReminded: false));
    }

    [Fact]
    public void DefaultMinutes_IsPartOfAllowedChoices()
    {
        Assert.Contains(PauseReminderRule.DefaultMinutes, PauseReminderRule.AllowedMinutes);
    }
}
