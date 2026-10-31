using System.Runtime.InteropServices;

namespace FocusManager.App.Native;

/// <summary>
/// 判断一个窗口是不是「真正的全屏」。
///
/// 三个条件同时满足才算：
/// 1. 窗口矩形铺满整个显示器（注意是显示器，不是去掉任务栏后的工作区——
///    所以普通「最大化」不算全屏）；
/// 2. 窗口没有标题栏、也没有可拖拽的边框；
/// 3. 窗口没有被 DWM 隐藏。
///
/// 另外会用 SHQueryUserNotificationState 做一次旁证：系统自己说「正在全屏
/// 运行 D3D 程序」时更确定。但这一项不作为必要条件，因为有些播放器的全屏
/// 并不会改变这个状态。
/// </summary>
public static class FullscreenDetector
{
    /// <summary>允许的像素误差（边框阴影、DPI 取整会造成 1~2 像素差）。</summary>
    private const int Tolerance = 2;

    public static bool IsFullscreen(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            if (!WindowsApi.IsWindowVisible(handle))
                return false;

            if (WindowsApi.IsCloaked(handle))
                return false;

            if (WindowsApi.HasCaptionOrFrame(handle))
                return false;

            if (!WindowsApi.GetWindowRect(handle, out var windowRect))
                return false;

            var monitor = WindowsApi.MonitorFromWindow(handle, WindowsApi.MONITOR_DEFAULTTONEAREST);

            if (monitor == IntPtr.Zero)
                return false;

            var monitorInfo = new WindowsApi.MonitorInfo
            {
                Size = Marshal.SizeOf<WindowsApi.MonitorInfo>()
            };

            if (!WindowsApi.GetMonitorInfo(monitor, ref monitorInfo))
                return false;

            if (!CoversWholeMonitor(windowRect, monitorInfo.Monitor))
                return false;

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 系统自己认为现在是否处于「不该打扰」的状态。
    /// 用于给 IsFullscreen 做旁证，也用于判断系统级全屏（比如演示模式）。
    /// </summary>
    public static bool IsSystemBusy()
    {
        try
        {
            if (WindowsApi.SHQueryUserNotificationState(out var state) != 0)
                return false;

            return state is UserNotificationState.Busy
                or UserNotificationState.RunningD3dFullScreen
                or UserNotificationState.PresentationMode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool CoversWholeMonitor(WindowsApi.Rect window, WindowsApi.Rect monitor)
        => Math.Abs(window.Left - monitor.Left) <= Tolerance
        && Math.Abs(window.Top - monitor.Top) <= Tolerance
        && Math.Abs(window.Right - monitor.Right) <= Tolerance
        && Math.Abs(window.Bottom - monitor.Bottom) <= Tolerance;
}
