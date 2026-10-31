using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FocusManager.App.Native;

/// <summary>前台窗口的一小份信息。只取判断需要的，不碰窗口内容。</summary>
/// <param name="Handle">窗口句柄。</param>
/// <param name="ProcessId">进程编号。</param>
/// <param name="ProcessName">进程名（不带 .exe）。取不到时是空字符串。</param>
/// <param name="WindowTitle">窗口标题。仅用于日志和调试显示。</param>
/// <param name="ClassName">窗口类名。</param>
public sealed record ForegroundWindowInfo(
    IntPtr Handle,
    uint ProcessId,
    string ProcessName,
    string WindowTitle,
    string ClassName);

/// <summary>用户通知状态（SHQueryUserNotificationState 的返回值）。</summary>
public enum UserNotificationState
{
    NotPresent = 1,
    Busy = 2,
    RunningD3dFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7
}

/// <summary>
/// Windows 系统调用的薄封装。
///
/// 只放真正用到的几个函数，不做通用封装。所有函数都只读窗口的
/// 句柄、类名、标题、位置这些元数据，**不读取任何窗口内容**。
/// </summary>
public static class WindowsApi
{
    public const int GWL_STYLE = -16;

    public const long WS_CAPTION = 0x00C00000L;
    public const long WS_THICKFRAME = 0x00040000L;

    /// <summary>前台窗口发生了变化。</summary>
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

    /// <summary>事件回调不注入到别的进程（必须用这个，否则会拖慢系统）。</summary>
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    /// <summary>忽略本程序自己进程的窗口。</summary>
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    public const int DWMWA_CLOAKED = 14;

    public delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    /// <summary>这个窗口是不是已经最小化了。</summary>
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    /// <summary>这个窗口是不是处于最大化状态。</summary>
    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    /// <summary>资源管理器 Alt+Tab 内部使用的切换方式，作为激活的最后兜底。</summary>
    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(IntPtr hWnd, bool altTab);

    /// <summary>敲一下 Alt 用的。Windows 只允许"最近有输入"的线程改前台窗口。</summary>
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

    public const int SW_SHOW = 5;

    /// <summary>ShowWindow 的参数：把最小化的窗口还原。</summary>
    public const int SW_RESTORE = 9;

    public const int SW_MINIMIZE = 6;

    public const int SW_MAXIMIZE = 3;

    public const byte VK_MENU = 0x12;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>枚举所有顶层窗口的回调；返回 false 表示停止枚举。</summary>
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventDelegate eventDelegate,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out UserNotificationState state);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    /// <summary>
    /// 取当前前台窗口的信息。取不到（比如桌面刚切换、进程已退出）时返回 null。
    /// </summary>
    public static ForegroundWindowInfo? GetForegroundWindowInfo()
    {
        var handle = GetForegroundWindow();

        if (handle == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(handle, out var processId);

        return new ForegroundWindowInfo(
            handle,
            processId,
            GetProcessName(processId),
            GetWindowTitle(handle),
            GetClassName(handle));
    }

    /// <summary>根据进程编号取进程名。取不到时返回空字符串，绝不抛异常。</summary>
    public static string GetProcessName(uint processId)
    {
        if (processId == 0)
            return string.Empty;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // 进程刚好退出、或者是受保护的系统进程，都取不到名字。
            return string.Empty;
        }
    }

    /// <summary>取窗口标题（只用于显示和排查，不读取内容）。</summary>
    public static string GetWindowTitle(IntPtr handle)
    {
        try
        {
            var length = GetWindowTextLength(handle);

            if (length <= 0)
                return string.Empty;

            var buffer = new StringBuilder(length + 1);
            GetWindowText(handle, buffer, buffer.Capacity);

            return buffer.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>取窗口类名。</summary>
    public static string GetClassName(IntPtr handle)
    {
        try
        {
            var buffer = new StringBuilder(256);
            GetClassName(handle, buffer, buffer.Capacity);

            return buffer.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>窗口是否带标题栏和可调整边框（普通窗口有，全屏播放器没有）。</summary>
    public static bool HasCaptionOrFrame(IntPtr handle)
    {
        try
        {
            var style = GetWindowLongPtr(handle, GWL_STYLE).ToInt64();

            return (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) == WS_THICKFRAME;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>窗口是否被 DWM 隐藏（比如「电影和电视」的幽灵窗口）。</summary>
    public static bool IsCloaked(IntPtr handle)
    {
        try
        {
            var result = DwmGetWindowAttribute(handle, DWMWA_CLOAKED, out var cloaked, sizeof(int));

            return result == 0 && cloaked != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
