using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FocusManager.App.Native;

/// <summary>
/// 调用一个 Windows 系统接口，把窗口标题栏也变成深色。
///
/// 为什么要这个：Windows 自己画的标题栏默认是浅色的。
/// 如果程序切到深色主题、标题栏还是白的，上面会像贴了一条白纸，很割裂。
///
/// 这个调用只改颜色，不做任何窗口改造，失败了也不影响程序运行，
/// 所以整段都包在 try/catch 里。
/// </summary>
public static class DwmHelper
{
    // Windows 10 20H1 之后用 20，更早的版本用 19。两个都试一遍最稳。
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>给一个已经显示出来的窗口设置标题栏深浅。</summary>
    public static void ApplyTitleBarTheme(Window? window, bool dark)
    {
        if (window is null)
        {
            return;
        }

        ApplyToHandle(new WindowInteropHelper(window).Handle, dark);
    }

    /// <summary>给所有已经创建的窗口重新设置一次（切换主题时调用）。</summary>
    public static void ApplyTitleBarTheme(bool dark)
    {
        var app = Application.Current;

        if (app is null)
        {
            return;
        }

        foreach (Window window in app.Windows)
        {
            ApplyTitleBarTheme(window, dark);
        }
    }

    private static void ApplyToHandle(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;

        try
        {
            var result = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int));

            if (result != 0)
            {
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // 极老的系统没有这个接口，忽略即可，标题栏保持系统默认颜色。
        }
        catch (EntryPointNotFoundException)
        {
            // 同上。
        }
    }
}
