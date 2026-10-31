namespace FocusManager.Core.Services;

/// <summary>
/// 「这一段离开」走到哪一步了。用显式状态代替零散的布尔量，
/// 免得出现「关掉提醒又把状态清零、于是再弹一次」这类循环。
/// </summary>
public enum FocusGuardState
{
    /// <summary>正处在允许的学习环境里。</summary>
    Normal,

    /// <summary>发现切到了未授权的软件，但还没提醒（比如当时在全屏）。</summary>
    UnauthorizedDetected,

    /// <summary>提醒已经显示过了，用户还在未授权的软件里。</summary>
    ReminderShown,

    /// <summary>用户点了「确认切换」，正在决定暂停还是结束。</summary>
    UserConfirmed
}

/// <summary>前台窗口变化之后，程序应该做什么。</summary>
public enum FocusGuardAction
{
    /// <summary>什么都不用做。</summary>
    None,

    /// <summary>提醒用户「你似乎离开了当前学习环境」。</summary>
    Remind,

    /// <summary>用户回到了允许的软件，把「这一段已经提醒过」的状态清掉。</summary>
    ReturnedToFocus
}

/// <summary>
/// 「要不要提醒用户离开学习环境」的判断逻辑。
///
/// 这里只管规则，不碰窗口、不弹窗、不读进程，所以可以单独测试。
/// 几条必须守住的行为：
/// 1. 同一段未经允许的使用过程只提醒一次，不重复打扰。
/// 2. 用户回到允许的软件之后，下一次离开要重新提醒（状态被重置）。
/// 3. 真正的全屏（比如全屏看视频）期间不弹提醒，先记下来，等退出全屏再说。
/// 4. 全屏期间用户自己回到了允许的软件，那条攒下的提醒就作废，不再补弹。
/// 5. 从不自动暂停、从不自动结束——这里只产出「提醒」这一个动作。
/// </summary>
public sealed class FocusGuardPolicy
{
    private readonly FocusAllowList _allowList;

    private bool _hasPendingReminder;

    public FocusGuardPolicy(FocusAllowList allowList)
        => _allowList = allowList ?? throw new ArgumentNullException(nameof(allowList));

    /// <summary>这一段离开目前走到哪一步。</summary>
    public FocusGuardState State { get; private set; } = FocusGuardState.Normal;

    /// <summary>当前这段「离开」是否已经提醒过。</summary>
    public bool WarnedForCurrentEpisode
        => State is FocusGuardState.ReminderShown or FocusGuardState.UserConfirmed;

    /// <summary>是否有因为全屏而延后的提醒，等着退出全屏后补发。</summary>
    public bool HasPendingReminder => _hasPendingReminder;

    /// <summary>本轮允许使用的软件名单。</summary>
    public FocusAllowList AllowList => _allowList;

    /// <summary>
    /// 根据「前台窗口属于哪个进程」和「这个窗口是不是真全屏」给出动作。
    ///
    /// 关键规则：本程序自己的窗口（离开提醒、暂停提醒、主界面）一律当作「中性」，
    /// 既不重置状态也不提醒。否则提醒窗口一抢到前台，就会被判成「回到了学习环境」，
    /// 状态被清零，用户还在别的程序里，于是又被判成一次新的离开——每 5 秒弹一次。
    /// </summary>
    public FocusGuardAction Evaluate(string? processName, bool isFullscreen)
    {
        if (_allowList.IsSelfProcess(processName))
            return FocusGuardAction.None;

        if (_allowList.IsAllowed(processName))
        {
            _hasPendingReminder = false;

            var wasReminded = WarnedForCurrentEpisode;
            State = FocusGuardState.Normal;

            return wasReminded ? FocusGuardAction.ReturnedToFocus : FocusGuardAction.None;
        }

        // 以下都是「不在允许名单里」的情况。

        if (isFullscreen)
        {
            // 全屏期间不打扰，先记下来。已经提醒过的那一段不用再攒。
            if (State == FocusGuardState.Normal)
            {
                State = FocusGuardState.UnauthorizedDetected;
                _hasPendingReminder = true;
            }

            return FocusGuardAction.None;
        }

        // 已经提醒过（或用户已经确认要切换）的，同一段里不再打扰。
        if (WarnedForCurrentEpisode)
        {
            _hasPendingReminder = false;
            return FocusGuardAction.None;
        }

        State = FocusGuardState.ReminderShown;
        _hasPendingReminder = false;

        return FocusGuardAction.Remind;
    }

    /// <summary>用户点了「确认切换」，后面由他自己决定暂停还是结束。</summary>
    public void MarkUserConfirmed()
    {
        if (State == FocusGuardState.ReminderShown)
            State = FocusGuardState.UserConfirmed;
    }

    /// <summary>开始新的一轮专注、或者用户暂停之后重新开始时调用。</summary>
    public void Reset()
    {
        State = FocusGuardState.Normal;
        _hasPendingReminder = false;
    }
}
