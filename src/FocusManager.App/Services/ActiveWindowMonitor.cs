using System.Windows.Threading;
using FocusManager.App.Native;

namespace FocusManager.App.Services;

/// <summary>
/// 盯着「现在最前面的是哪个窗口」。
///
/// 用 Windows 的前台窗口事件通知（SetWinEventHook）来触发，不是每秒去问系统
/// 一次——那样既费电又容易漏掉瞬间切换。事件到来后先等 400 毫秒再判断，
/// 这样 Alt+Tab 过程中一闪而过的窗口、系统弹的小面板就不会造成误报。
///
/// 另外每 5 秒做一次成本极低的兜底检查：万一某次事件没收到（比如系统繁忙时
/// 丢事件），也能在一个心跳内纠正过来；它顺便负责发现「同一个窗口从全屏变回
/// 窗口」——这种情况没有前台切换事件，但延后的提醒需要在这时补上。
/// 这个检查只在句柄变化或全屏状态变化时才真正去取进程名，平时几乎不消耗 CPU。
/// </summary>
public sealed class ActiveWindowMonitor : IDisposable
{
    /// <summary>事件到来后等这么久再判断，避开 Alt+Tab 的中间态。</summary>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// 兜底检查间隔。除了防止漏事件，它还负责发现「同一个窗口从全屏变回窗口」——
    /// 这种情况不会产生前台窗口切换事件，但对专注提醒很重要：全屏期间被延后的
    /// 提醒，要等用户退出全屏后补上。
    /// </summary>
    public static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _fallbackTimer;
    private readonly WindowsApi.WinEventDelegate _callback;

    private IntPtr _hook;
    private IntPtr _lastHandle = IntPtr.Zero;
    private bool _lastFullscreen;
    private bool _disposed;

    public ActiveWindowMonitor()
    {
        // 委托必须保存在字段里，否则会被回收，系统回调时就崩了。
        _callback = OnWinEvent;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DebounceDelay
        };
        _debounceTimer.Tick += OnDebounceTick;

        _fallbackTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FallbackInterval
        };
        _fallbackTimer.Tick += OnFallbackTick;
    }

    /// <summary>前台窗口换成了另一个程序（已去抖）。</summary>
    public event EventHandler<ForegroundWindowInfo>? ForegroundChanged;

    /// <summary>是否已经在监听。</summary>
    public bool IsRunning => _hook != IntPtr.Zero;

    /// <summary>开始监听。</summary>
    public void Start()
    {
        if (_disposed || IsRunning)
            return;

        try
        {
            _hook = WindowsApi.SetWinEventHook(
                WindowsApi.EVENT_SYSTEM_FOREGROUND,
                WindowsApi.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WindowsApi.WINEVENT_OUTOFCONTEXT | WindowsApi.WINEVENT_SKIPOWNPROCESS);
        }
        catch (Exception)
        {
            _hook = IntPtr.Zero;
        }

        _lastHandle = IntPtr.Zero;
        _lastFullscreen = false;
        _fallbackTimer.Start();

        // 启动时先报一次当前状态，不用等用户切换。
        _debounceTimer.Start();
    }

    /// <summary>停止监听。</summary>
    public void Stop()
    {
        _debounceTimer.Stop();
        _fallbackTimer.Stop();

        if (_hook != IntPtr.Zero)
        {
            try
            {
                WindowsApi.UnhookWinEvent(_hook);
            }
            catch (Exception)
            {
                // 取消监听失败也没什么可做的。
            }

            _hook = IntPtr.Zero;
        }

        _lastHandle = IntPtr.Zero;
        _lastFullscreen = false;
    }

    /// <summary>立刻重新读一次前台窗口（比如用户刚点了「返回项目」）。</summary>
    public void Refresh()
    {
        if (_disposed)
            return;

        _debounceTimer.Stop();
        Report();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();

        _debounceTimer.Tick -= OnDebounceTick;
        _fallbackTimer.Tick -= OnFallbackTick;
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (_disposed || eventType != WindowsApi.EVENT_SYSTEM_FOREGROUND)
            return;

        // 事件回调在 UI 线程上（OUTOFCONTEXT），重启去抖计时器即可。
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        Report();
    }

    private void OnFallbackTick(object? sender, EventArgs e)
    {
        var handle = WindowsApi.GetForegroundWindow();

        // 句柄没变时，只在「全屏状态变了」才重新判断。
        // 这样用户看完全屏视频退出全屏的那一刻，被延后的提醒就会补上，
        // 而平时几乎不消耗资源。
        if (handle == _lastHandle && FullscreenDetector.IsFullscreen(handle) == _lastFullscreen)
            return;

        Report();
    }

    private void Report()
    {
        var info = WindowsApi.GetForegroundWindowInfo();

        if (info is null)
            return;

        // 本程序自己的窗口（主界面、离开提醒、暂停提醒）不参与「离开学习环境」的判断。
        // SetWinEventHook 的 SKIPOWNPROCESS 只管事件，管不住下面这个每 5 秒的兜底检查，
        // 所以这里必须自己挡一次，否则提醒窗口抢到前台就会把状态搅乱。
        if (info.ProcessId == (uint)Environment.ProcessId)
            return;

        _lastHandle = info.Handle;
        _lastFullscreen = FullscreenDetector.IsFullscreen(info.Handle);

        ForegroundChanged?.Invoke(this, info);
    }
}
