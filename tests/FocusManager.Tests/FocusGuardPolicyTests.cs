using FocusManager.Core.Services;

namespace FocusManager.Tests;

public class FocusGuardPolicyTests
{
    private static FocusGuardPolicy CreatePolicy(string primary = "CCtalk")
        => new(new FocusAllowList(primary));

    [Fact]
    public void AllowedWindow_DoesNothing()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.None, policy.Evaluate("CCtalk", isFullscreen: false));
        Assert.False(policy.WarnedForCurrentEpisode);
    }

    [Fact]
    public void UnauthorizedWindow_RemindsOnlyOnce()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("Notepad", isFullscreen: false));
    }

    [Fact]
    public void ReturningToAllowedWindow_ReportsIt()
    {
        var policy = CreatePolicy();

        policy.Evaluate("chrome", isFullscreen: false);

        Assert.Equal(FocusGuardAction.ReturnedToFocus, policy.Evaluate("Obsidian", isFullscreen: false));
        Assert.False(policy.WarnedForCurrentEpisode);
    }

    [Fact]
    public void AfterReturning_NextUnauthorizedWindowRemindsAgain()
    {
        var policy = CreatePolicy();

        policy.Evaluate("chrome", isFullscreen: false);
        policy.Evaluate("Obsidian", isFullscreen: false);

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
    }

    [Fact]
    public void FullscreenUnauthorizedWindow_DefersReminder()
    {
        var policy = CreatePolicy();

        // 全屏看视频：先不打扰。
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("PotPlayer", isFullscreen: true));
        Assert.True(policy.HasPendingReminder);
        Assert.False(policy.WarnedForCurrentEpisode);

        // 退出全屏之后补一次提醒。
        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("PotPlayer", isFullscreen: false));
        Assert.False(policy.HasPendingReminder);
    }

    [Fact]
    public void FullscreenThenBackToAllowedWindow_DropsPendingReminder()
    {
        var policy = CreatePolicy();

        policy.Evaluate("PotPlayer", isFullscreen: true);
        Assert.True(policy.HasPendingReminder);

        // 用户自己回到了允许的软件，那条攒下的提醒就不该再弹。
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("CCtalk", isFullscreen: false));
        Assert.False(policy.HasPendingReminder);
    }

    [Fact]
    public void DeferredFullscreenReminder_IsDeliveredAfterLeavingFullscreen()
    {
        var policy = new FocusGuardPolicy(new FocusAllowList("CCtalk"));

        // 用户在看全屏视频：先不打扰。
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("SomePlayer", isFullscreen: true));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("SomePlayer", isFullscreen: true));

        // 退出全屏后，同一个窗口仍然不在学习环境里——这时候才提醒。
        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("SomePlayer", isFullscreen: false));

        // 提醒过之后，同一次离开不再重复打扰。
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("SomePlayer", isFullscreen: false));
    }

    [Fact]
    public void FullscreenAfterBeingWarned_DoesNotQueueAnotherReminder()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: true));

        Assert.False(policy.HasPendingReminder);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var policy = CreatePolicy();

        policy.Evaluate("chrome", isFullscreen: false);
        policy.Reset();

        Assert.False(policy.WarnedForCurrentEpisode);
        Assert.False(policy.HasPendingReminder);
        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
    }

    [Fact]
    public void SelfWindow_DoesNotResetTheEpisode()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));

        // 提醒窗口抢到前台，前台进程变成我们自己的程序。
        // 这既不是「回到学习环境」，也不是新的「离开」，状态必须原样保留。
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("ProjectFocusManager", isFullscreen: false));
        Assert.True(policy.WarnedForCurrentEpisode);
        Assert.Equal(FocusGuardState.ReminderShown, policy.State);

        // 用户还停在 chrome 里：不能再弹第二次。
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: false));
    }

    [Fact]
    public void SelfWindow_WhileInAllowedEnvironment_DoesNothing()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.None, policy.Evaluate("CCtalk", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("ProjectFocusManager", isFullscreen: false));
        Assert.Equal(FocusGuardState.Normal, policy.State);
    }

    [Fact]
    public void SwitchingBetweenUnauthorizedApps_RemindsOnlyOnce()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("Notepad", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("steam", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("ProjectFocusManager", isFullscreen: false));
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: false));
    }

    [Fact]
    public void UserConfirmed_StopsFurtherRemindersForThisEpisode()
    {
        var policy = CreatePolicy();

        policy.Evaluate("chrome", isFullscreen: false);
        policy.MarkUserConfirmed();

        Assert.Equal(FocusGuardState.UserConfirmed, policy.State);
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: false));

        // 回到允许的软件之后，下一段离开才重新提醒。
        Assert.Equal(FocusGuardAction.ReturnedToFocus, policy.Evaluate("CCtalk", isFullscreen: false));
        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
    }

    [Fact]
    public void State_MovesThroughExpectedSteps()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardState.Normal, policy.State);

        policy.Evaluate("SomePlayer", isFullscreen: true);
        Assert.Equal(FocusGuardState.UnauthorizedDetected, policy.State);

        policy.Evaluate("SomePlayer", isFullscreen: false);
        Assert.Equal(FocusGuardState.ReminderShown, policy.State);

        policy.Evaluate("CCtalk", isFullscreen: false);
        Assert.Equal(FocusGuardState.Normal, policy.State);
    }

    [Fact]
    public void UnknownProcess_IsTreatedAsUnauthorized()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate(null, isFullscreen: false));
    }

    [Fact]
    public void UpdateAllowList_NewProcessStopsReminding()
    {
        var policy = CreatePolicy();

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));

        // 用户在提醒窗口里点了「添加辅助程序」，chrome 从此算本轮环境的一部分。
        policy.UpdateAllowList(new FocusAllowList("CCtalk", ["chrome"]));

        Assert.Equal(FocusGuardAction.ReturnedToFocus, policy.Evaluate("chrome", isFullscreen: false));
        Assert.False(policy.WarnedForCurrentEpisode);
    }

    [Fact]
    public void UpdateAllowList_KeepsStateSoTheReminderIsNotRepeated()
    {
        var policy = CreatePolicy();

        policy.Evaluate("chrome", isFullscreen: false);
        Assert.Equal(FocusGuardState.ReminderShown, policy.State);

        // 加的是另一个程序，当前这段离开的处理状态不该被清掉。
        policy.UpdateAllowList(new FocusAllowList("CCtalk", ["Obsidian"]));

        Assert.Equal(FocusGuardState.ReminderShown, policy.State);
        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: false));
    }

    [Fact]
    public void UpdateAllowList_DroppingAProcessRemindsAgain()
    {
        var policy = new FocusGuardPolicy(new FocusAllowList("CCtalk", ["chrome"]));

        Assert.Equal(FocusGuardAction.None, policy.Evaluate("chrome", isFullscreen: false));

        // 用户把辅助程序移掉了，下一次切过去要重新提醒。
        policy.UpdateAllowList(new FocusAllowList("CCtalk"));

        Assert.Equal(FocusGuardAction.Remind, policy.Evaluate("chrome", isFullscreen: false));
    }
}
