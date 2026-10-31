using FocusManager.App.Native;
using FocusManager.Core.Services;

namespace FocusManager.App.Services;

/// <summary>
/// 把「前台窗口监听」和「要不要提醒」的规则接起来。
///
/// 分工：ActiveWindowMonitor 负责知道前台换了什么窗口，
/// FocusGuardPolicy 负责判断该不该提醒（纯逻辑，有单元测试），
/// 这个类只做中间那一层接线，外加把全屏判断塞进去。
///
/// 它永远只产生一个后果：弹一个轻量提醒。不会自动暂停、不会自动结束、
/// 不会关掉别人的程序、不会锁屏。
/// </summary>
public sealed class FocusGuardService : IDisposable
{
    private readonly ActiveWindowMonitor _monitor;

    private FocusGuardPolicy? _policy;
    private bool _disposed;

    public FocusGuardService(ActiveWindowMonitor? monitor = null)
    {
        _monitor = monitor ?? new ActiveWindowMonitor();
        _monitor.ForegroundChanged += OnForegroundChanged;
    }

    /// <summary>检测到用户切到了本轮不允许的软件。同一段只触发一次。</summary>
    public event EventHandler<ForegroundWindowInfo>? LeftFocusDetected;

    /// <summary>用户回到了允许的软件。</summary>
    public event EventHandler<ForegroundWindowInfo>? ReturnedToFocus;

    /// <summary>是否正在监听。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>开始一轮专注时调用，传入本轮的白名单。</summary>
    public void Start(FocusAllowList allowList)
    {
        _policy = new FocusGuardPolicy(allowList);
        IsRunning = true;

        _monitor.Start();
    }

    /// <summary>结束（或暂停）这一轮专注时调用，停止监听。</summary>
    public void Stop()
    {
        IsRunning = false;
        _policy = null;

        _monitor.Stop();
    }

    /// <summary>
    /// 用户从提醒里点了「返回项目」之后调用：重新判断一次当前窗口，
    /// 并把「这一段已经提醒过」的状态留着，避免立刻又弹一次。
    /// </summary>
    /// <summary>
    /// 用户点了提醒里的「确认切换」：这一段离开已经处理过了，后面不再提醒。
    /// </summary>
    public void MarkUserConfirmed() => _policy?.MarkUserConfirmed();

    public void RefreshNow() => _monitor.Refresh();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _monitor.ForegroundChanged -= OnForegroundChanged;
        _monitor.Dispose();
    }

    private void OnForegroundChanged(object? sender, ForegroundWindowInfo info)
    {
        var policy = _policy;

        if (policy is null || !IsRunning)
            return;

        var isFullscreen = FullscreenDetector.IsFullscreen(info.Handle);
        var action = policy.Evaluate(info.ProcessName, isFullscreen);

        switch (action)
        {
            case FocusGuardAction.Remind:
                LeftFocusDetected?.Invoke(this, info);
                break;

            case FocusGuardAction.ReturnedToFocus:
                ReturnedToFocus?.Invoke(this, info);
                break;
        }
    }
}
