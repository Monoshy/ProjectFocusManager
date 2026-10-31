using System.Diagnostics;
using FocusManager.App.Native;

namespace FocusManager.App.Services;

/// <summary>激活学习应用失败的原因。每一种都给用户一句能看懂的话。</summary>
public enum ActivationFailure
{
    /// <summary>成功了。</summary>
    None,

    /// <summary>这一轮没有记录主要学习软件。</summary>
    NotRecorded,

    /// <summary>程序已经关掉了。</summary>
    NotRunning,

    /// <summary>进程还在，但没有可见的窗口（比如收进了托盘）。</summary>
    NoVisibleWindow,

    /// <summary>找到了窗口，但 Windows 不允许自动切到前台。</summary>
    CannotBringToFront
}

/// <summary>激活的结果。</summary>
public sealed record ActivationResult(bool Success, ActivationFailure Reason, string Message)
{
    public static ActivationResult Ok() => new(true, ActivationFailure.None, string.Empty);
}

/// <summary>
/// 把「本轮主要学习软件」叫到前台。
///
/// 铁律：**只激活已经在运行的程序，绝不启动任何东西**。
/// 这里没有 Process.Start，也没有用登记的 EXE 路径去启动，
/// 更不会去碰 Obsidian 的 Vault —— 找不到就如实告诉用户，让他自己打开。
/// </summary>
public static class ApplicationActivator
{
    public static ActivationResult Activate(string? processName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return new ActivationResult(
                false,
                ActivationFailure.NotRecorded,
                "这一轮没有记录主要学习软件，直接回到项目继续吧。");
        }

        var windows = FindWindows(processName);

        if (windows.Count == 0)
        {
            // 再确认一次到底是"没开"还是"开了但没窗口"，这样提示才准确。
            var processRunning = IsProcessRunning(processName);

            return processRunning
                ? new ActivationResult(
                    false,
                    ActivationFailure.NoVisibleWindow,
                    $"{displayName} 正在运行，但没有可以显示出来的窗口（可能收进了托盘）。请把它打开后再继续。")
                : new ActivationResult(
                    false,
                    ActivationFailure.NotRunning,
                    $"{displayName} 已经关闭，请手动打开应用后再回来继续。");
        }

        // 优先挑没有最小化的窗口，其次才是最小化的。
        var candidates = windows
            .OrderBy(h => WindowsApi.IsIconic(h) ? 1 : 0)
            .ToList();

        foreach (var handle in candidates)
        {
            if (WindowsApi.IsIconic(handle))
                WindowsApi.ShowWindow(handle, WindowsApi.SW_RESTORE);

            if (ForegroundHelper.ForceActivate(handle))
                return ActivationResult.Ok();
        }

        return new ActivationResult(
            false,
            ActivationFailure.CannotBringToFront,
            $"已经找到 {displayName} 的窗口，但 Windows 的防抢焦点机制拦了一下，没能自动切过去（这是 Windows 对后台程序的统一限制，不是出错）。"
            + "可以自己点一下任务栏上的它，再点「我已打开，继续专注」；或者直接点「直接继续计时」，先把这一轮恢复起来。");
    }

    /// <summary>按进程名找所有「有标题的可见顶层窗口」。</summary>
    private static List<IntPtr> FindWindows(string processName)
    {
        var normalized = FocusManager.Core.Services.FocusAllowList.Normalize(processName);
        var result = new List<IntPtr>();

        if (normalized.Length == 0)
            return result;

        WindowsApi.EnumWindows((handle, _) =>
        {
            if (!WindowsApi.IsWindowVisible(handle))
                return true;

            if (WindowsApi.GetWindowTextLength(handle) == 0)
                return true;

            WindowsApi.GetWindowThreadProcessId(handle, out var processId);

            if (processId == 0)
                return true;

            var name = WindowsApi.GetProcessName(processId);

            if (string.Equals(FocusManager.Core.Services.FocusAllowList.Normalize(name), normalized, StringComparison.OrdinalIgnoreCase))
                result.Add(handle);

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsProcessRunning(string processName)
    {
        var normalized = FocusManager.Core.Services.FocusAllowList.Normalize(processName);

        if (normalized.Length == 0)
            return false;

        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName(normalized);
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
