using FocusManager.Core.Time;

namespace FocusManager.Tests;

/// <summary>时间显示格式的测试。保证界面上的数字不会算错、不会显示成怪样子。</summary>
public sealed class TimeFormatterTests
{
    [Theory]
    [InlineData(0, 0, 0, "00:00:00")]
    [InlineData(0, 15, 32, "00:15:32")]
    [InlineData(1, 25, 38, "01:25:38")]
    [InlineData(3, 0, 0, "03:00:00")]
    [InlineData(100, 0, 0, "100:00:00")]
    public void ToClock_ShowsHoursMinutesSeconds(int hours, int minutes, int seconds, string expected)
    {
        var value = new TimeSpan(hours, minutes, seconds);
        Assert.Equal(expected, TimeFormatter.ToClock(value));
    }

    [Fact]
    public void ToClock_NegativeValue_ShowsZero()
    {
        Assert.Equal("00:00:00", TimeFormatter.ToClock(TimeSpan.FromSeconds(-5)));
    }

    [Theory]
    [InlineData(0, "0 秒")]
    [InlineData(45, "45 秒")]
    [InlineData(60, "1 分钟")]
    [InlineData(720, "12 分钟")]
    [InlineData(5100, "1 小时 25 分钟")]      // 85 分钟
    [InlineData(10800, "3 小时")]             // 3 小时整
    [InlineData(90, "1 分钟 30 秒")]
    public void ToChinese_ReadsNaturally(int totalSeconds, string expected)
    {
        Assert.Equal(expected, TimeFormatter.ToChinese(TimeSpan.FromSeconds(totalSeconds)));
    }

    [Fact]
    public void ToChinese_NegativeValue_ShowsZero()
    {
        Assert.Equal("0 秒", TimeFormatter.ToChinese(TimeSpan.FromSeconds(-30)));
    }

    [Fact]
    public void ToLocalDateTimeText_UsesChineseDateFormat()
    {
        var value = new DateTimeOffset(2026, 10, 31, 14, 36, 0, TimeSpan.Zero);
        var text = TimeFormatter.ToLocalDateTimeText(value);

        // 不同机器的时区不同，这里只检查格式骨架，不写死具体时刻。
        Assert.Matches(@"^\d{4}年\d{1,2}月\d{1,2}日 \d{2}:\d{2}$", text);
    }

    [Fact]
    public void ToLocalDateText_ShowsMonthAndDay()
    {
        var value = new DateTimeOffset(2026, 10, 31, 14, 36, 0, TimeSpan.Zero);
        Assert.Matches(@"^\d{1,2}月\d{1,2}日$", TimeFormatter.ToLocalDateText(value));
    }
}
