using System.Diagnostics;
using FocusManager.App.Native;

namespace FocusManager.Tests;

/// <summary>
/// 前台激活的真实 Win32 集成测试。
///
/// 这是 App 层少数能在无人值守下验证的行为：起一个 winver 窗口，把它最小化，
/// 再用 ForegroundHelper 抢前台，确认 Windows 真的把前台交给了它。
/// 这个场景就是「暂停 5 分钟后点回到项目，学习软件却切不过去」的最小复现。
///
/// 注意：测试会短暂动一下真实桌面（弹一个小窗口又关掉），需要在交互会话里跑。
/// </summary>
public class ForegroundHelperTests
{
    [Fact]
    public void ForceActivate_MinimizedWindow_IsBroughtToFront()
    {
        using var process = StartNotepadWithWindow();

        if (process is null)
        {
            // 找不到记事本的环境（精简系统）跳过，不算失败。
            return;
        }

        try
        {
            var handle = WaitForMainWindow(process);

            Assert.NotEqual(IntPtr.Zero, handle);

            WindowsApi.ShowWindow(handle, WindowsApi.SW_MINIMIZE);

            var activated = ForegroundHelper.ForceActivate(handle);

            Assert.True(activated, "ForceActivate 应当成功，哪怕目标窗口处于最小化状态。");
            Assert.Equal(handle, WindowsApi.GetForegroundWindow());
        }
        finally
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // 进程已经自己退出了就算了。
            }
        }
    }

    /// <summary>
    /// 启动一个经典 Win32 窗口程序当靶子。
    /// 用 winver 而不是记事本：Win11 的记事本是被 ApplicationFrameHost 托管的打包应用，
    /// 进程自己拿不到顶层窗口句柄；winver 在所有 Windows 上都是经典窗口。
    /// 启动失败（无该程序、无桌面会话）返回 null。
    /// </summary>
    private static Process? StartNotepadWithWindow()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo("winver.exe")
            {
                UseShellExecute = true,
            });

            return process;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>记事本的窗口不是一启动就有，最多等 5 秒。</summary>
    private static IntPtr WaitForMainWindow(Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();

            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;

            Thread.Sleep(100);
        }

        return IntPtr.Zero;
    }
}
