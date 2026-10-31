using FocusManager.Core.Services;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void SetThenGet_RoundTrips()
    {
        using var db = new TempDatabase();
        var settings = new SettingsService(db.Service);

        settings.Set("Theme", "Dark");

        Assert.Equal("Dark", settings.Get("Theme"));
    }

    [Fact]
    public void Get_MissingKeyReturnsNull()
    {
        using var db = new TempDatabase();
        var settings = new SettingsService(db.Service);

        Assert.Null(settings.Get("从来没有写过的设置"));
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        using var db = new TempDatabase();
        var settings = new SettingsService(db.Service);

        settings.Set("Theme", "Dark");
        settings.Set("Theme", "Light");

        Assert.Equal("Light", settings.Get("Theme"));
    }

    [Fact]
    public void GetBool_FallsBackWhenMissing()
    {
        using var db = new TempDatabase();
        var settings = new SettingsService(db.Service);

        Assert.True(settings.GetBool("SomeFlag", fallback: true));
        Assert.False(settings.GetBool("SomeFlag", fallback: false));
    }

    [Fact]
    public void SetBool_RoundTrips()
    {
        using var db = new TempDatabase();
        var settings = new SettingsService(db.Service);

        settings.SetBool("SomeFlag", true);

        Assert.True(settings.GetBool("SomeFlag", fallback: false));

        settings.SetBool("SomeFlag", false);

        Assert.False(settings.GetBool("SomeFlag", fallback: true));
    }

    [Fact]
    public void GetBool_UsesFallbackWhenValueIsNotABoolean()
    {
        using var db = new TempDatabase();
        var settings = new SettingsService(db.Service);

        settings.Set("SomeFlag", "这不是布尔值");

        Assert.True(settings.GetBool("SomeFlag", fallback: true));
    }
}
