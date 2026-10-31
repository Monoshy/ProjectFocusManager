namespace FocusManager.Core.Services;

/// <summary>点主窗口右上角关闭按钮时怎么办。</summary>
public enum CloseBehavior
{
    /// <summary>每次都问。</summary>
    Ask,

    /// <summary>藏到系统托盘，继续计时。</summary>
    MinimizeToTray,

    /// <summary>直接退出程序。</summary>
    Exit
}

/// <summary>
/// 「关闭按钮怎么处理」这条设置的读写规则。
///
/// 放在 Core 里是为了能单独测试：数据库里存的是字符串，
/// 万一以后手改了数据库或者降级了程序，读出来是乱七八糟的值也不能崩，
/// 一律回退到「每次询问」。
/// </summary>
public static class CloseBehaviorRule
{
    /// <summary>设置项的名字（AppSettings 表的 Key）。</summary>
    public const string SettingKey = "CloseAction";

    /// <summary>默认行为：每次询问。</summary>
    public const CloseBehavior Default = CloseBehavior.Ask;

    /// <summary>数据库里允许出现的字符串值。</summary>
    public static readonly string[] AllowedValues = ["Ask", "Tray", "Exit"];

    /// <summary>把数据库里的字符串翻译成行为。认不出来就用默认值。</summary>
    public static CloseBehavior Resolve(string? value)
        => value switch
        {
            "Tray" => CloseBehavior.MinimizeToTray,
            "Exit" => CloseBehavior.Exit,
            _ => Default
        };

    /// <summary>把行为翻译成存进数据库的字符串。</summary>
    public static string ToSettingValue(CloseBehavior behavior)
        => behavior switch
        {
            CloseBehavior.MinimizeToTray => "Tray",
            CloseBehavior.Exit => "Exit",
            _ => "Ask"
        };

    /// <summary>界面上给用户看的文字。</summary>
    public static string ToDisplayName(CloseBehavior behavior)
        => behavior switch
        {
            CloseBehavior.MinimizeToTray => "最小化到系统托盘",
            CloseBehavior.Exit => "直接退出程序",
            _ => "每次询问"
        };
}
