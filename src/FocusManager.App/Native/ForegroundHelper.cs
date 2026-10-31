using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FocusManager.App.Native;

/// <summary>
/// 把某个窗口强制拉到最前面并给它键盘焦点。
///
/// Windows 默认不允许后台程序抢前台（防止恶意弹窗），所以这里用
/// AttachThreadInput 临时把自己挂到当前前台线程上，借它的权限完成切换，
/// 做完立刻解开。失败时静默放弃，不抛异常——最坏情况就是窗口只是被显示出来。
/// </summary>
public static class ForegroundHelper
{
    public static void ForceActivate(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
                return;

            ForceActivateCore(handle);

            // WPF 在 Loaded 之后还会自己做一次激活，可能把前台又还回去，
            // 所以等消息队列空下来之后再抢一次，这样才真的稳。
            window.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        ForceActivateCore(handle);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                    {
                    }
                }),
                DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // 老系统上缺 API 就退化成普通显示。
        }
    }

    /// <summary>
    /// 按窗口句柄抢前台。激活别的程序时用这个重载。
    ///
    /// SetForegroundWindow 是异步生效的：调用完马上查前台，很可能还没切过去，
    /// 所以要先轮询确认一小段时间。确认确实没切过去，再走两级回退——
    /// 最小化再还原（系统在还原窗口时会把前台交给它，等价于用户点任务栏），
    /// 最后是 SwitchToThisWindow（资源管理器 Alt+Tab 用的那一招）。
    /// 三招都试过才算真的失败，否则会把"正在切换"误报成"Windows 不允许"。
    /// </summary>
    public static bool ForceActivate(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            ForceActivateCore(handle);

            if (WaitUntilForeground(handle))
                return true;

            var wasMaximized = WindowsApi.IsZoomed(handle);

            WindowsApi.ShowWindow(handle, WindowsApi.SW_MINIMIZE);
            WindowsApi.ShowWindow(handle, wasMaximized ? WindowsApi.SW_MAXIMIZE : WindowsApi.SW_RESTORE);

            if (WaitUntilForeground(handle))
                return true;

            WindowsApi.SwitchToThisWindow(handle, altTab: true);

            return WaitUntilForeground(handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>在约 250 毫秒内反复确认前台是否已经是目标窗口。</summary>
    private static bool WaitUntilForeground(IntPtr handle)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (WindowsApi.GetForegroundWindow() == handle)
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    private static void ForceActivateCore(IntPtr handle)
    {
        {
            var foreground = WindowsApi.GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero
                ? 0
                : WindowsApi.GetWindowThreadProcessId(foreground, out _);

            var currentThread = WindowsApi.GetCurrentThreadId();
            var attached = foregroundThread != 0
                && foregroundThread != currentThread
                && WindowsApi.AttachThreadInput(currentThread, foregroundThread, true);

            try
            {
                // 先假装用户刚按了一下 Alt：这样 Windows 认为我们"有输入"，
                // 才允许我们改前台窗口，否则 SetForegroundWindow 会被直接忽略。
                WindowsApi.keybd_event(WindowsApi.VK_MENU, 0, 0, IntPtr.Zero);
                WindowsApi.ShowWindow(handle, WindowsApi.SW_SHOW);
                WindowsApi.BringWindowToTop(handle);
                WindowsApi.SetForegroundWindow(handle);
                WindowsApi.SetFocus(handle);
                WindowsApi.keybd_event(WindowsApi.VK_MENU, 0, WindowsApi.KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                if (attached)
                    WindowsApi.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
