using FocusManager.Core.Time;

namespace FocusManager.Core.Services;

/// <summary>
/// 计时器当前所处的状态。
/// </summary>
public enum TimerState
{
    /// <summary>空闲，还没开始。</summary>
    Idle,

    /// <summary>正在专注计时。</summary>
    Running,

    /// <summary>已暂停，不计入有效专注时间。</summary>
    Paused,

    /// <summary>已结束（结果已经取出）。</summary>
    Stopped
}

/// <summary>
/// 一段已经完成的有效专注时间的快照（值对象，不可修改）。
/// </summary>
public sealed record FocusIntervalSnapshot(DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, TimeSpan Duration);

/// <summary>
/// 一次已经结束的暂停的快照。
/// </summary>
public sealed record PauseSnapshot(DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, TimeSpan Duration, string Reason);

/// <summary>
/// 一次专注会话结束后的完整结果。
/// </summary>
public sealed record FocusSessionResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    TimeSpan EffectiveDuration,
    TimeSpan PausedDuration,
    IReadOnlyList<FocusIntervalSnapshot> Intervals,
    IReadOnlyList<PauseSnapshot> Pauses)
{
    public int PauseCount => Pauses.Count;

    public int IntervalCount => Intervals.Count;

    /// <summary>有效专注秒数（四舍五入到整秒，用于入库）。</summary>
    public long EffectiveSeconds => (long)Math.Round(EffectiveDuration.TotalSeconds, MidpointRounding.AwayFromZero);

    /// <summary>暂停秒数（四舍五入到整秒，用于入库）。</summary>
    public long PausedSeconds => (long)Math.Round(PausedDuration.TotalSeconds, MidpointRounding.AwayFromZero);
}

/// <summary>
/// 正向计时核心。
///
/// 设计要点（这是本项目可靠性的关键）：
/// 1. 时间来源是 <see cref="ITimeSource.MonotonicNow"/>（单调时钟），不是「每秒加一」。
///    界面上的数字只是「读取当前时长再显示」，即使界面卡顿、电脑休眠、系统时间被改，
///    下一次读取都会算出正确的时长。
/// 2. 时间被切成「有效段」：开始→暂停 是一段，恢复→结束 是另一段。
///    暂停期间没有段在走，所以暂停时间天然不会被算进有效时间。
/// 3. 每次一段结束（暂停、结束）都会触发事件，让上层立即把这一段写进数据库，
///    这样即使程序突然崩溃，已经完成的段也不会丢。
/// 4. 这个类不依赖数据库、不依赖界面，所以可以被单元测试完整覆盖。
/// </summary>
public sealed class FocusTimerService
{
    /// <summary>短于这个长度的有效段不单独记一条（避免 0 秒碎片记录）。</summary>
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    private readonly ITimeSource _time;
    private readonly List<FocusIntervalSnapshot> _intervals = new();
    private readonly List<PauseSnapshot> _pauses = new();

    private TimerState _state = TimerState.Idle;
    private DateTimeOffset _startedAtUtc;
    private DateTimeOffset _segmentStartUtc;
    private TimeSpan _segmentStartMonotonic;
    private DateTimeOffset _pauseStartUtc;
    private TimeSpan _pauseStartMonotonic;
    private string _currentPauseReason = string.Empty;

    /// <summary>当前是否有一段「正在走」的有效时间。冻结时会变成 false。</summary>
    private bool _segmentOpen;

    /// <summary>冻结发生的时刻（UTC），用于结束时间取「用户按下结束」的那一刻。</summary>
    private DateTimeOffset _frozenAtUtc;

    public FocusTimerService(ITimeSource? timeSource = null)
    {
        _time = timeSource ?? new SystemTimeSource();
    }

    /// <summary>一段有效专注时间结束时触发（暂停、结束都会触发）。</summary>
    public event EventHandler<FocusIntervalSnapshot>? IntervalCompleted;

    /// <summary>一次暂停结束时触发（恢复、结束都会触发）。</summary>
    public event EventHandler<PauseSnapshot>? PauseCompleted;

    public TimerState State => _state;

    /// <summary>是否处于「正在进行中」（计时或暂停）的状态。</summary>
    public bool IsActive => _state is TimerState.Running or TimerState.Paused;

    public bool IsRunning => _state == TimerState.Running;

    public bool IsPaused => _state == TimerState.Paused;

    /// <summary>
    /// 是否处于「冻结」状态：会话还在，但时间不再往上涨。
    /// 用在「点结束 → 弹出总结窗口填写内容」这段时间，避免写总结的时间被算成专注时间。
    /// </summary>
    public bool IsFrozen => _state == TimerState.Running && !_segmentOpen;

    /// <summary>本次会话的开始时间（UTC）。</summary>
    public DateTimeOffset StartedAtUtc => _startedAtUtc;

    /// <summary>当前暂停的开始时间；不在暂停中时抛异常。</summary>
    public DateTimeOffset CurrentPauseStartedAtUtc
    {
        get
        {
            if (_state != TimerState.Paused)
                throw new InvalidOperationException("当前不在暂停状态。");
            return _pauseStartUtc;
        }
    }

    /// <summary>当前暂停的原因；不在暂停中时抛异常。</summary>
    public string CurrentPauseReason
    {
        get
        {
            if (_state != TimerState.Paused)
                throw new InvalidOperationException("当前不在暂停状态。");
            return _currentPauseReason;
        }
    }

    /// <summary>
    /// 累计有效专注时间 = 已完成各段之和 + （如果正在计时）当前这一段。
    /// 界面每次刷新时读这个属性即可，不需要自己累加。
    /// </summary>
    public TimeSpan EffectiveDuration
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var interval in _intervals)
            {
                total += interval.Duration;
            }

            if (_state == TimerState.Running && _segmentOpen)
            {
                var current = _time.MonotonicNow - _segmentStartMonotonic;
                if (current > TimeSpan.Zero) total += current;
            }

            return total;
        }
    }

    /// <summary>
    /// 累计暂停时间 = 已结束的暂停之和 + （如果正在暂停）当前这次暂停。
    /// </summary>
    public TimeSpan PausedDuration
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var pause in _pauses)
            {
                total += pause.Duration;
            }

            if (_state == TimerState.Paused)
            {
                var current = _time.MonotonicNow - _pauseStartMonotonic;
                if (current > TimeSpan.Zero) total += current;
            }

            return total;
        }
    }

    public int PauseCount => _pauses.Count + (_state == TimerState.Paused ? 1 : 0);

    public IReadOnlyList<FocusIntervalSnapshot> CompletedIntervals => _intervals;

    public IReadOnlyList<PauseSnapshot> CompletedPauses => _pauses;

    /// <summary>
    /// 开始一轮专注。只能在空闲状态调用。
    /// </summary>
    public void Start()
    {
        if (_state is TimerState.Running or TimerState.Paused)
            throw new InvalidOperationException("专注已经在进行中，不能重复开始。");

        if (_state == TimerState.Stopped)
            Reset();

        _startedAtUtc = _time.UtcNow;
        _segmentStartUtc = _startedAtUtc;
        _segmentStartMonotonic = _time.MonotonicNow;
        _segmentOpen = true;
        _state = TimerState.Running;
    }

    /// <summary>
    /// 暂停。必须提供原因（界面会先让用户填写）。
    /// 暂停后不再累计有效专注时间，也不会自动恢复。
    /// </summary>
    public void Pause(string reason)
    {
        if (_state != TimerState.Running)
            throw new InvalidOperationException("当前没有正在进行的专注，不能暂停。");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("暂停原因不能为空。", nameof(reason));

        CloseCurrentInterval();

        _currentPauseReason = reason.Trim();
        _pauseStartUtc = _time.UtcNow;
        _pauseStartMonotonic = _time.MonotonicNow;
        _state = TimerState.Paused;
    }

    /// <summary>
    /// 从暂停恢复计时。开始一段新的有效专注段。
    /// </summary>
    public void Resume()
    {
        if (_state != TimerState.Paused)
            throw new InvalidOperationException("当前不处于暂停状态，不能恢复。");

        CloseCurrentPause();

        _segmentStartUtc = _time.UtcNow;
        _segmentStartMonotonic = _time.MonotonicNow;
        _segmentOpen = true;
        _state = TimerState.Running;
    }

    /// <summary>
    /// 结束本次专注，返回完整结果。暂停中直接结束也是允许的（暂停时长会被记下来）。
    /// </summary>
    public FocusSessionResult Stop()
    {
        if (_state == TimerState.Idle)
            throw new InvalidOperationException("还没有开始专注，无法结束。");

        if (_state == TimerState.Stopped)
            throw new InvalidOperationException("本次专注已经结束了。");

        // 先记下「是否处于冻结」，因为下面会把状态改成 Stopped，之后就判断不出来了。
        var wasFrozen = IsFrozen;

        if (_state == TimerState.Running)
        {
            CloseCurrentInterval();
        }
        else if (_state == TimerState.Paused)
        {
            CloseCurrentPause();
        }

        _state = TimerState.Stopped;

        var endedAtUtc = wasFrozen ? _frozenAtUtc : _time.UtcNow;

        return new FocusSessionResult(
            _startedAtUtc,
            endedAtUtc,
            EffectiveDuration,
            PausedDuration,
            _intervals.ToArray(),
            _pauses.ToArray());
    }

    /// <summary>
    /// 把当前正在进行的这一段「先存下来」，然后接着计新的一段。
    ///
    /// 为什么需要它：正常情况下一段只在暂停/结束时才写进数据库，
    /// 如果程序突然崩溃或断电，正在进行的这一段就会丢失。
    /// 界面每隔一分钟调用一次这个方法，最坏情况也只丢一分钟。
    ///
    /// <paramref name="minimumSegment"/> 用来避免产生大量几秒钟的碎片段。
    /// </summary>
    public void FlushCurrentInterval(TimeSpan minimumSegment)
    {
        if (_state != TimerState.Running || !_segmentOpen) return;

        var elapsed = _time.MonotonicNow - _segmentStartMonotonic;
        if (elapsed < minimumSegment) return;

        CloseCurrentInterval();

        _segmentStartUtc = _time.UtcNow;
        _segmentStartMonotonic = _time.MonotonicNow;
        _segmentOpen = true;
    }

    /// <summary>
    /// 冻结计时：把正在走的这一段立刻结算并停住，之后 <see cref="EffectiveDuration"/> 不再增长。
    /// 用途是「用户点了结束 → 弹出总结窗口写内容」这段时间不应该算成专注时间。
    /// 如果用户在总结窗口里点了「继续专注」，调用 <see cref="Unfreeze"/> 恢复。
    /// </summary>
    public void Freeze()
    {
        if (_state != TimerState.Running || !_segmentOpen) return;

        _frozenAtUtc = _time.UtcNow;
        CloseCurrentInterval(_frozenAtUtc);
    }

    /// <summary>
    /// 解除冻结，从「现在」重新开一段。冻结期间的时间不会被计入。
    /// </summary>
    public void Unfreeze()
    {
        if (_state != TimerState.Running || _segmentOpen) return;

        _segmentStartUtc = _time.UtcNow;
        _segmentStartMonotonic = _time.MonotonicNow;
        _segmentOpen = true;
    }

    /// <summary>
    /// 清空状态，回到空闲。结束一次会话并保存后调用，准备下一轮。
    /// </summary>
    public void Reset()
    {
        _intervals.Clear();
        _pauses.Clear();
        _state = TimerState.Idle;
        _startedAtUtc = default;
        _segmentStartUtc = default;
        _segmentStartMonotonic = TimeSpan.Zero;
        _pauseStartUtc = default;
        _pauseStartMonotonic = TimeSpan.Zero;
        _currentPauseReason = string.Empty;
        _segmentOpen = false;
        _frozenAtUtc = default;
    }

    private void CloseCurrentInterval(DateTimeOffset? endedAtUtc = null)
    {
        if (!_segmentOpen) return;

        var duration = _time.MonotonicNow - _segmentStartMonotonic;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        // 不足 1 秒的碎片段直接丢掉，避免数据库里出现一堆 0 秒的记录
        // （比如刚点开始就点结束、或者刚继续就暂停）。
        if (duration < MinimumInterval)
        {
            _segmentOpen = false;
            return;
        }

        var snapshot = new FocusIntervalSnapshot(_segmentStartUtc, endedAtUtc ?? _time.UtcNow, duration);
        _intervals.Add(snapshot);
        _segmentOpen = false;
        IntervalCompleted?.Invoke(this, snapshot);
    }

    private void CloseCurrentPause()
    {
        var duration = _time.MonotonicNow - _pauseStartMonotonic;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        var snapshot = new PauseSnapshot(_pauseStartUtc, _time.UtcNow, duration, _currentPauseReason);
        _pauses.Add(snapshot);
        _currentPauseReason = string.Empty;
        PauseCompleted?.Invoke(this, snapshot);
    }
}
