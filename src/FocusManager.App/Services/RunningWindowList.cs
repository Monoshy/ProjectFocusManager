using System.Diagnostics;
using FocusManager.App.Native;
using FocusManager.Core.Services;

namespace FocusManager.App.Services;

/// <summary>一个正在运行的、有可见窗口的程序。</summary>
public sealed record RunningWindow(string ProcessName, string Title, uint ProcessId, string? ExecutablePath);

/// <summary>
/// 列出当前正在运行、且有可见窗口的程序，供「从正在运行的程序中添加」使用。
///
/// 只在用户点按钮时调用一次，不常驻、不定时轮询。
/// </summary>
public static class RunningWindowList
{
    public static IReadOnlyList<RunningWindow> GetVisibleWindows()
    {
        var result = new List<RunningWindow>();
        var seenProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        WindowsApi.EnumWindows((handle, _) =>
        {
            if (!WindowsApi.IsWindowVisible(handle)) return true;

            var title = WindowsApi.GetWindowTitle(handle).Trim();

            if (title.Length == 0) return true;

            WindowsApi.GetWindowThreadProcessId(handle, out var processId);

            if (processId == 0) return true;

            var processName = WindowsApi.GetProcessName(processId);

            if (processName.Length == 0) return true;

            // 自己的窗口没必要出现在列表里。
            if (string.Equals(processName, FocusAllowList.SelfProcessName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 同一个程序开多个窗口时只列一条，够用了。
            if (!seenProcessNames.Add(processName)) return true;

            result.Add(new RunningWindow(processName, title, processId, TryGetExecutablePath(processId)));

            return true;
        }, IntPtr.Zero);

        return result
            .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 读取可执行文件路径。受保护的程序（比如以管理员权限运行的）会拒绝访问，
    /// 这时返回 null——识别窗口靠的是进程名，没有路径也能用。
    /// </summary>
    private static string? TryGetExecutablePath(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);

            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
