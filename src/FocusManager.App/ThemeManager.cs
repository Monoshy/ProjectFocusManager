using System.Windows;
using FocusManager.App.Native;
using FocusManager.Core.Services;

namespace FocusManager.App;

/// <summary>
/// 负责在浅色和深色之间切换。
///
/// 做法很简单：App.xaml 里的第 0 本资源字典就是主题，
/// 换主题时把这一本换成另一本，界面上的颜色会自动跟着变。
/// 换完再把窗口标题栏也刷一遍，免得深色界面顶着一条白标题栏。
/// </summary>
public static class ThemeManager
{
    public const string ThemeSettingKey = "Theme";

    public const string Light = "Light";

    public const string Dark = "Dark";

    /// <summary>主题换完之后触发，界面上的文字（比如"当前：深色"）靠它更新。</summary>
    public static event EventHandler? ThemeChanged;

    public static bool IsDark { get; private set; }

    /// <summary>给界面显示用的中文名字。</summary>
    public static string DisplayName => IsDark ? "深色模式" : "浅色模式";

    public static void Apply(string theme)
    {
        var useDark = string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase);

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(useDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative),
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }

        IsDark = useDark;

        DwmHelper.ApplyTitleBarTheme(useDark);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyFromSettings(SettingsService settings)
        => Apply(settings.Get(ThemeSettingKey) ?? Light);

    /// <summary>切换主题并记住选择，下次打开程序还是这个主题。</summary>
    public static void Toggle(SettingsService settings)
    {
        var next = IsDark ? Light : Dark;

        Apply(next);
        settings.Set(ThemeSettingKey, next);
    }

    /// <summary>指定一个主题并记住选择。</summary>
    public static void Set(SettingsService settings, bool dark)
    {
        var next = dark ? Dark : Light;

        if (IsDark == dark)
        {
            return;
        }

        Apply(next);
        settings.Set(ThemeSettingKey, next);
    }
}
