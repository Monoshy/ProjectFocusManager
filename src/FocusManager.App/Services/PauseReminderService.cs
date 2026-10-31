using System.Windows.Threading;
using FocusManager.Core.Services;

namespace FocusManager.App.Services;

/// <summary>
/// 暂停计时器：用户暂停之后开始数，数到设定的分钟数就提醒一次。
///
/// 只提醒一次，不重复催；用户继续专注或结束这一轮时直接取消。
/// 这个类不碰界面，只负责「时间到了」这件事，界面自己决定怎么提醒。
/// </summary>
public sealed class PauseReminderService : IDisposable
{
    private readonly DispatcherTimer _timer;

    private bool _reminded;
    private bool _disposed;

    public PauseReminderService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
    }

    /// <summary>时间到了。</summary>
    public event EventHandler? ReminderDue;

    /// <summary>是否正在等这个提醒。</summary>
    public bool IsWaiting => _timer.IsEnabled;

    /// <summary>
    /// 开始等待。threshold 为 null 表示用户关掉了这个提醒，直接不启动。
    /// </summary>
    public void Start(TimeSpan? threshold)
    {
        Stop();

        if (_disposed || threshold is null || threshold.Value <= TimeSpan.Zero)
            return;

        _reminded = false;
        _timer.Interval = threshold.Value;
        _timer.Start();
    }

    /// <summary>取消等待（继续专注、结束这一轮、或者用户自己回来了）。</summary>
    public void Stop()
    {
        _timer.Stop();
        _reminded = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (_reminded)
            return;

        _reminded = true;
        ReminderDue?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按当前暂停时长判断要不要马上提醒（比如从托盘恢复界面时补一次判断）。</summary>
    public bool ShouldRemindNow(TimeSpan paused, TimeSpan? threshold)
        => PauseReminderRule.ShouldRemind(paused, threshold, _reminded);
}
