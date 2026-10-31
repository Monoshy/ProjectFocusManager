namespace FocusManager.Core.Services;

/// <summary>
/// 判断「当前前台窗口属于哪个程序」之后，这个程序算不算本轮允许使用的软件。
///
/// 规则（来自需求）：
/// 1. 本轮选的主要学习应用算允许。
/// 2. Obsidian 一直算允许（它是通用笔记工具，随时可能要查笔记）。
/// 3. 程序自己算允许，否则用户一操作自己的界面就被判成「离开了」。
/// 4. 必要的 Windows 系统交互窗口算允许（开始菜单、任务栏、输入法、锁屏等），
///    这些不是「去玩了」，不提醒。
/// 5. 其它已经登记在应用管理里的软件，**不会**因为登记过就自动获得本轮的权限。
///    比如本轮选了 CCtalk，那百度网盘即使登记过也不算允许。
///
/// 这个类只做纯字符串判断，不碰任何 Windows API，方便单元测试。
/// </summary>
public sealed class FocusAllowList
{
    /// <summary>Obsidian 的进程名，永远允许。</summary>
    public const string ObsidianProcessName = "Obsidian";

    /// <summary>本程序的进程名，永远允许。</summary>
    public const string SelfProcessName = "ProjectFocusManager";

    /// <summary>
    /// 必要的 Windows 系统交互窗口。这些窗口出现时不算「离开学习环境」。
    /// 只列真正必要的，不把任务管理器、设置之类放进来。
    /// </summary>
    private static readonly string[] SystemProcessNames =
    [
        "explorer",                  // 桌面、任务栏、文件资源管理器
        "ShellExperienceHost",       // 开始菜单、音量、网络等系统面板
        "StartMenuExperienceHost",   // 开始菜单（Win10/11）
        "SearchHost",                // 搜索面板
        "SearchApp",                 // 搜索（旧版）
        "SearchUI",                  // 搜索（更旧）
        "LockApp",                   // 锁屏
        "TextInputHost",             // 输入法候选窗
        "ApplicationFrameHost",      // UWP 应用外壳（部分系统面板走这里）
        "sihost",                    // shell 基础结构
        "dwm",                       // 桌面窗口管理器
        "Taskmgr"                    // 任务管理器：用户主动排障，不该被当成娱乐
    ];

    private readonly HashSet<string> _allowed;

    public FocusAllowList(string? primaryProcessName, IEnumerable<string>? extraAllowedProcessNames = null)
    {
        PrimaryProcessName = Normalize(primaryProcessName);

        _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SelfProcessName,
            ObsidianProcessName
        };

        foreach (var name in SystemProcessNames)
            _allowed.Add(name);

        if (PrimaryProcessName.Length > 0)
            _allowed.Add(PrimaryProcessName);

        if (extraAllowedProcessNames is not null)
        {
            foreach (var name in extraAllowedProcessNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _allowed.Add(Normalize(name));
            }
        }
    }

    /// <summary>本轮选的主要学习应用（已规范化的进程名）。没选就是空字符串。</summary>
    public string PrimaryProcessName { get; }

    /// <summary>
    /// 这个进程名是不是本程序自己。
    ///
    /// 为什么要单独判断：自己的窗口（比如离开提醒）拿到前台时，既不算「回到学习环境」，
    /// 也不算「离开了」。如果把它当成允许环境，提醒窗口一抢到前台就会把「已提醒」
    /// 状态清掉，用户还在别的程序里，于是又被判成一次新的离开——变成每 5 秒弹一次。
    /// </summary>
    public bool IsSelfProcess(string? processName)
        => string.Equals(Normalize(processName), SelfProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>这个进程名算不算允许。</summary>
    public bool IsAllowed(string? processName)
    {
        var normalized = Normalize(processName);

        if (normalized.Length == 0)
            return false;

        return _allowed.Contains(normalized);
    }

    /// <summary>
    /// 是不是必要的系统窗口。系统窗口即使不在允许列表里，也不该提醒，
    /// 但通常已经包含在 IsAllowed 里了，这个方法留给调用方做额外判断。
    /// </summary>
    public static bool IsSystemProcess(string? processName)
    {
        var normalized = Normalize(processName);

        if (normalized.Length == 0)
            return false;

        foreach (var name in SystemProcessNames)
        {
            if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>去掉可能的 .exe 后缀和首尾空白。</summary>
    public static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return string.Empty;

        var trimmed = processName.Trim();

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        return trimmed;
    }
}
