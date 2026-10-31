namespace FocusManager.Core.Models;

/// <summary>
/// 一个已注册的学习应用（例如百度网盘、CCtalk、Obsidian）。
/// 注意：注册到这里不等于「本次专注允许使用」，每轮专注只允许主应用 + Obsidian + 内置 ChatGPT。
/// </summary>
public sealed class ApplicationInfo
{
    public long Id { get; set; }

    /// <summary>显示名称，例如「百度网盘」。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>可执行文件完整路径，例如 C:\Program Files\...\BaiduNetdisk.exe。</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>进程名称（不含 .exe），例如 BaiduNetdisk。</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>图标缓存文件路径（可以为空）。</summary>
    public string? IconPath { get; set; }

    /// <summary>是否为程序内置应用（首次启动自动创建，可停用但不可删除）。</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>是否启用。停用后不会出现在选择列表里。</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public override string ToString() => Name;
}
