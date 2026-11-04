using FocusManager.Core.Services;

namespace FocusManager.Tests;

/// <summary>
/// 「不到 5 分钟不记录」的阈值判断。
/// </summary>
public sealed class SessionRecordingRulesTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(301, true)]
    [InlineData(3600, true)]
    public void ShouldRecord_FollowsFiveMinuteRule(long seconds, bool expected)
        => Assert.Equal(expected, SessionRecordingRules.ShouldRecord(seconds));

    [Fact]
    public void MinimumEffectiveSeconds_IsFiveMinutes()
        => Assert.Equal(300, SessionRecordingRules.MinimumEffectiveSeconds);

    [Theory]
    [InlineData(299.4, false)]
    [InlineData(299.6, true)]
    [InlineData(300, true)]
    public void ShouldRecord_OnTimeSpan_RoundsToNearestSecond(double seconds, bool expected)
        => Assert.Equal(expected, SessionRecordingRules.ShouldRecord(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void BuildDiscardMessage_MentionsDurationAndRule()
    {
        var message = SessionRecordingRules.BuildDiscardMessage(125);

        Assert.Contains("2 分钟", message);
        Assert.Contains("不到 5 分钟", message);
    }
}
