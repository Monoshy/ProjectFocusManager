using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FocusManager.App.Native;
using FocusManager.App.Services;
using FocusManager.App.ViewModels;
using FocusManager.App.Views.Dialogs;
using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Core.Time;

namespace FocusManager.App.Views;

/// <summary>
/// 专注页面的界面部分。
///
/// 这里只做三件事：把 ViewModel 挂上去、「弹哪个窗、拿到结果后交给谁」，
/// 以及管住「前台窗口提醒」的开关。真正的业务判断都在 FocusViewModel 和 Core 里的服务中。
/// </summary>
public partial class FocusView : UserControl
{
    private readonly FocusViewModel _viewModel;

    private readonly PauseReminderService _pauseReminder = new();

    /// <summary>
    /// 专注期间每隔一段时间把当前进度写进数据库。
    /// 有了它，程序被强制关掉或者断电时，最多只丢半分钟，而不是整轮。
    /// </summary>
    private readonly DispatcherTimer _checkpointTimer = new()
    {
        Interval = TimeSpan.FromSeconds(15),
    };

    private FocusGuardService? _guard;
    private LeftFocusWindow? _reminderWindow;
    private PauseReminderWindow? _pauseReminderWindow;

    public FocusView()
    {
        InitializeComponent();

        var services = AppServices.Current
            ?? throw new InvalidOperationException("程序服务还没有初始化。");

        _viewModel = new FocusViewModel(services);
        DataContext = _viewModel;

        // 页面看不见时不用每 200 毫秒刷新显示，省一点电。
        IsVisibleChanged += (_, _) => _viewModel.SetDisplayActive(IsVisible);

        // 只有「开始 / 暂停 / 继续 / 结束」这类状态变化才通知外面（托盘图标要跟着变）。
        // 计时数字每 200 毫秒变一次，那个不需要通知。
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FocusViewModel.IsIdle)
                or nameof(FocusViewModel.IsRunning)
                or nameof(FocusViewModel.IsPaused))
            {
                FocusStateChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        _pauseReminder.ReminderDue += (_, _) => ShowPauseReminder();

        _checkpointTimer.Tick += (_, _) => SaveCheckpoint();
    }

    /// <summary>专注状态变了（开始、暂停、继续、结束），托盘图标需要刷新。</summary>
    public event EventHandler? FocusStateChanged;

    /// <summary>切换回这个页面时刷新一下列表（可能刚在别的页面新建了项目）。</summary>
    public void RefreshFromDatabase()
    {
        _viewModel.ReloadProjects();
        _viewModel.ReloadApplications();
        _viewModel.RefreshDisplay();
    }

    /// <summary>启动时恢复了上次没结束的那一轮之后调用，把计时状态重新显示出来。</summary>
    public void RefreshAfterRecovery() => _viewModel.RefreshAfterRecovery();

    private void OnNewProjectClick(object sender, RoutedEventArgs e)
    {
        var dialog = new NewProjectWindow { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var project = AppServices.Current!.Projects.Create(dialog.ProjectName, dialog.ProjectDescription);

            _viewModel.ReloadProjects();
            _viewModel.SelectedProject = _viewModel.Projects.FirstOrDefault(p => p.Id == project.Id);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.StartSession();

            StartFocusGuard();
            StartCheckpointTimer();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnPauseClick(object sender, RoutedEventArgs e) => PauseWithReason();

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.ResumeSession();

            StopPauseReminder();
            StartFocusGuard();
            StartCheckpointTimer();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnFinishClick(object sender, RoutedEventArgs e) => FinishCurrentSession();

    /// <summary>暂停：必须先写原因。从「暂停一下」按钮和「离开提醒」里都会走到这里。</summary>
    private void PauseWithReason()
    {
        var dialog = new PauseReasonWindow { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _viewModel.PauseSession(dialog.Reason);

            // 暂停期间用户本来就会去做别的事，这时候再提醒「你离开了」没有意义。
            StopFocusGuard();

            StopCheckpointTimer();

            StartPauseReminder();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    /// <summary>结束这一轮：先冻结时间，再让用户写总结。</summary>
    private void FinishCurrentSession()
    {
        var services = AppServices.Current!;
        var timer = services.Sessions.Timer;

        if (timer is null)
        {
            ShowError("当前没有正在进行的专注。");
            return;
        }

        StopFocusGuard();
        StopPauseReminder();
        StopCheckpointTimer();

        var projectName = _viewModel.CurrentProjectName;

        // 先把时间停住：接下来写总结用掉的时间不应该算成专注时间。
        // 如果用户在总结窗口里点了「继续专注」，再恢复计时。
        _viewModel.FreezeForSummary();

        var dialog = new SessionSummaryWindow(
            projectName,
            timer.EffectiveDuration,
            timer.PausedDuration,
            timer.PauseCount,
            ideaCount: CountIdeasOfCurrentSession())
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() != true)
        {
            // 用户点了取消：恢复计时，继续这一轮。
            _viewModel.ResumeAfterSummary();

            if (timer.IsRunning)
            {
                StartFocusGuard();
            }

            return;
        }

        try
        {
            // 不到 5 分钟的一轮，默认不留记录。但也不硬性丢掉——
            // 有人就是想在开始前先记一笔，所以问一句，让他自己定。
            if (!SessionRecordingRules.ShouldRecord(dialog.EffectiveDuration))
            {
                var discard = AppMessage.Confirm(
                    Window.GetWindow(this),
                    $"这一轮有效专注 {TimeFormatter.ToChinese(dialog.EffectiveDuration)}，不到 5 分钟。"
                    + "按规则这种不记进历史——要作废，还是仍然保存下来？",
                    "这一轮要记下来吗？",
                    okText: "作废这一轮",
                    cancelText: "仍然保存",
                    kind: MessageKind.Warning);

                if (discard)
                {
                    _viewModel.DiscardSession();

                    AppMessage.Show(
                        Window.GetWindow(this),
                        "这一轮已经作废，没有记进项目时间。",
                        "作废好了",
                        MessageKind.Success);

                    return;
                }
            }

            _viewModel.FinishSession(dialog.ProgressNote, dialog.ReflectionNote);

            AppMessage.Show(
                Window.GetWindow(this),
                "有效专注时间：" + TimeFormatter.ToChinese(dialog.EffectiveDuration) + "\n" +
                "项目累计时间已经更新，可以在「项目记录」里查看。",
                "这一轮已经保存好了",
                MessageKind.Success);
        }
        catch (Exception ex)
        {
            // 保存失败时如果计时还停在冻结状态，先恢复计时，
            // 否则界面会显示「正在专注」但数字不动，用户只能再点一次结束。
            if (timer.IsFrozen)
            {
                _viewModel.ResumeAfterSummary();

                if (timer.IsRunning)
                {
                    StartFocusGuard();
                }
            }

            ShowError(ex.Message);
        }
    }

    // ==================== 随手记 ====================

    /// <summary>结束总结里显示的「本次记了几条想法」。</summary>
    private int CountIdeasOfCurrentSession()
    {
        var services = AppServices.Current;
        if (services is null) return 0;

        var sessionId = services.Sessions.CurrentSessionId;
        return sessionId is null ? 0 : services.Ideas.CountBySession(sessionId.Value);
    }

    private void OnQuickIdeaClick(object sender, RoutedEventArgs e) => SaveQuickIdea();

    private void OnQuickIdeaTextChanged(object sender, TextChangedEventArgs e)
    {
        // 开始写新的想法时，把上一次「已记下」的提示清掉，免得看着像没保存。
        if (QuickIdeaHint is not null && QuickIdeaBox.Text.Length > 0)
            QuickIdeaHint.Text = string.Empty;
    }

    private void OnQuickIdeaKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveQuickIdea();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 专注中记一笔：记到当前项目、当前这一轮上，不打断计时。
    /// </summary>
    private void SaveQuickIdea()
    {
        var services = AppServices.Current;
        if (services is null) return;

        if (string.IsNullOrWhiteSpace(QuickIdeaBox.Text))
        {
            QuickIdeaHint.Text = "写一句再记下来。";
            QuickIdeaBox.Focus();
            return;
        }

        if (services.Sessions.CurrentProjectId is not { } projectId)
        {
            QuickIdeaHint.Text = "现在没有进行中的专注，记不了。";
            return;
        }

        try
        {
            var note = services.Ideas.Add(projectId, QuickIdeaBox.Text, services.Sessions.CurrentSessionId);
            QuickIdeaBox.Text = string.Empty;
            QuickIdeaHint.Text = "已记下（" + TimeFormatter.ToLocalDateTimeText(note.CreatedAtUtc) + "），继续专注吧。";
            QuickIdeaBox.Focus();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            QuickIdeaHint.Text = ex.Message;
        }
    }

    // ==================== 前台窗口提醒 ====================

    /// <summary>
    /// 开始监听前台窗口。只有在「正在专注」且选了本轮主要应用时才启用。
    /// 没选应用时不猜，也不提醒——否则一上来就会误报。
    /// </summary>
    private void StartFocusGuard()
    {
        var processName = _viewModel.CurrentApplicationProcessName;

        if (string.IsNullOrWhiteSpace(processName)) return;

        _guard ??= CreateGuard();

        _guard.Start(new FocusAllowList(processName, _viewModel.CurrentAuxiliaryProcessNames));
    }

    /// <summary>
    /// 本轮加了新的辅助程序之后，把白名单同步过去。
    /// 不重建 guard 是因为重建会丢掉「这一段离开已经处理过」的状态，
    /// 结果就是刚点完确认切换又弹一次。
    /// </summary>
    private void RefreshAllowList()
    {
        if (_guard is null || !_guard.IsRunning) return;

        var processName = _viewModel.CurrentApplicationProcessName;

        if (string.IsNullOrWhiteSpace(processName)) return;

        _guard.UpdateAllowList(new FocusAllowList(processName, _viewModel.CurrentAuxiliaryProcessNames));
    }

    private void StopFocusGuard()
    {
        _guard?.Stop();

        CloseReminder();
    }

    private FocusGuardService CreateGuard()
    {
        var guard = new FocusGuardService();

        guard.LeftFocusDetected += (_, info) => ShowReminder(info);
        guard.ReturnedToFocus += (_, _) => CloseReminder();

        return guard;
    }

    private void ShowReminder(ForegroundWindowInfo info)
    {
        if (_reminderWindow is not null) return;

        var window = new LeftFocusWindow(info.WindowTitle)
        {
            Owner = VisibleOwner,
        };

        window.ReturnRequested += (_, _) => _guard?.RefreshNow();

        // 这两个动作都会弹模态窗口。等提醒窗口自己先关掉再弹，
        // 免得两个窗口叠在一起。
        window.SwitchConfirmed += (_, _) => _guard?.MarkUserConfirmed();

        window.PauseRequested += (_, _) => Dispatcher.BeginInvoke(
            new Action(PauseWithReason), DispatcherPriority.Background);

        window.FinishRequested += (_, _) => Dispatcher.BeginInvoke(
            new Action(FinishCurrentSession), DispatcherPriority.Background);

        // 「添加辅助程序」：默认选中刚才那个窗口对应的程序，用户不用在列表里翻。
        window.AddApplicationRequested += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => AddAuxiliaryApplication(info.ProcessName)), DispatcherPriority.Background);

        window.Dismissed += (_, _) => _reminderWindow = null;

        _reminderWindow = window;

        window.Show();
    }

    private void CloseReminder()
    {
        var window = _reminderWindow;

        if (window is null) return;

        _reminderWindow = null;

        window.Close();
    }

    // ==================== 辅助程序 ====================

    private void OnAddAuxiliaryClick(object sender, RoutedEventArgs e) => AddAuxiliaryApplication();

    /// <summary>
    /// 加一个辅助程序进本轮。
    /// 空闲态下是「给这个项目配上」，运行中则是「本轮现在就允许它」——
    /// 后者会顺手把项目也挂上，下次开始这个项目时自动带上。
    /// </summary>
    private void AddAuxiliaryApplication(string? preselectProcessName = null)
    {
        var services = AppServices.Current;
        if (services is null) return;

        var alreadyAdded = new List<long>();

        if (services.Sessions.CurrentSessionId is { } sessionId)
        {
            alreadyAdded.AddRange(services.Sessions.GetSessionApplications(sessionId)
                .Where(row => row.ApplicationId.HasValue)
                .Select(row => row.ApplicationId!.Value));
        }
        else
        {
            alreadyAdded.AddRange(_viewModel.AuxiliaryApplications
                .Where(a => a.Id > 0)
                .Select(a => a.Id));
        }

        if (_viewModel.SelectedApplication is { } primary)
            alreadyAdded.Add(primary.Id);

        var picker = new ApplicationPickerWindow(alreadyAdded, preselectProcessName)
        {
            Owner = Window.GetWindow(this),
        };

        if (picker.ShowDialog() != true || picker.SelectedApplication is not { } chosen)
            return;

        try
        {
            _viewModel.AddAuxiliaryApplication(chosen.Id);

            RefreshAllowList();

            AppMessage.Show(
                Window.GetWindow(this),
                $"「{chosen.Name}」已经加进本轮了，切到它不会再弹提醒。",
                "加好了",
                MessageKind.Success);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnRemoveAuxiliaryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ApplicationInfo application)
            return;

        try
        {
            _viewModel.RemoveAuxiliaryApplication(application.Id);

            RefreshAllowList();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // ==================== 作废这一轮 ====================

    private void OnDiscardClick(object sender, RoutedEventArgs e) => DiscardCurrentSession();

    /// <summary>
    /// 作废：这一轮整个丢掉，不记时间也不留记录。
    /// 用错了就找不回来，所以要问一次。
    /// </summary>
    private void DiscardCurrentSession()
    {
        var services = AppServices.Current;
        if (services is null) return;

        if (services.Sessions.CurrentSessionId is null)
        {
            ShowError("当前没有正在进行的专注。");
            return;
        }

        var elapsed = services.Sessions.Timer?.EffectiveDuration ?? TimeSpan.Zero;

        var confirmed = AppMessage.Confirm(
            Window.GetWindow(this),
            $"这一轮已经专注了 {TimeFormatter.ToChinese(elapsed)}。作废之后不算进项目时间，也不会留下这条记录，撤不回来。",
            "作废这一轮？",
            okText: "作废",
            cancelText: "继续专注",
            kind: MessageKind.Error);

        if (!confirmed) return;

        StopFocusGuard();
        StopPauseReminder();
        StopCheckpointTimer();

        try
        {
            _viewModel.DiscardSession();

            AppMessage.Show(
                Window.GetWindow(this),
                "这一轮已经作废，没有记进项目时间。",
                "作废好了",
                MessageKind.Success);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // ==================== 暂停提醒 ====================

    /// <summary>
    /// 暂停之后开始数时间。数到用户设置的分钟数（默认 5 分钟）就提醒一次。
    /// 用户关掉这个提醒的话，这里什么都不做。
    /// </summary>
    private void StartPauseReminder()
    {
        var settings = AppServices.Current?.Settings;

        if (settings is null) return;

        _pauseReminder.Start(PauseReminderRule.ResolveThreshold(settings.Get(PauseReminderRule.SettingKey)));
    }

    private void StopPauseReminder()
    {
        _pauseReminder.Stop();

        ClosePauseReminder();
    }

    private void ShowPauseReminder()
    {
        if (_pauseReminderWindow is not null) return;

        if (!_viewModel.IsPaused) return;

        var timer = AppServices.Current?.Sessions.Timer;
        var window = new PauseReminderWindow(_viewModel.CurrentProjectName, timer?.PausedDuration ?? TimeSpan.Zero)
        {
            Owner = VisibleOwner,
        };

        window.ResumeRequested += (_, _) => Dispatcher.BeginInvoke(
            new Action(() => ResumeFromReminder(showWindow: true)), DispatcherPriority.Background);

        window.FinishRequested += (_, _) => Dispatcher.BeginInvoke(
            new Action(FinishCurrentSession), DispatcherPriority.Background);

        window.Closed += (_, _) => _pauseReminderWindow = null;

        _pauseReminderWindow = window;

        window.Show();
    }

    private void ClosePauseReminder()
    {
        var window = _pauseReminderWindow;

        if (window is null) return;

        _pauseReminderWindow = null;

        window.Close();
    }

    /// <summary>
    /// 点了「回到项目」之后要做的三件事：
    /// 1. 把本轮主要学习软件叫到前台（**只激活已经开着的，绝不替用户启动程序**）；
    /// 2. 确认真的切过去了，才恢复计时；
    /// 3. 应用没开就弹提示，让用户自己打开，然后可以再试一次。
    /// 任何一步没成，这一轮都保持在暂停状态，不会偷偷开始计时。
    /// 成功切过去之后不会再激活主窗口，否则会把前台又抢回来。
    /// </summary>
    private void ResumeFromReminder(bool showWindow = false)
    {
        var processName = _viewModel.CurrentApplicationProcessName;
        var displayName = _viewModel.CurrentApplicationName;

        var activation = ApplicationActivator.Activate(processName, displayName);

        if (!activation.Success && activation.Reason != ActivationFailure.NotRecorded)
        {
            AskUserToOpenApplication(activation.Message);

            return;
        }

        // 只有在「这一轮根本没记录学习软件」的时候才把主窗口叫出来。
        // 如果刚刚已经把学习软件切到前台了，再 Activate 主窗口就等于把前台又抢回来，
        // 用户会看到窗口一闪又回到程序自己身上。
        if (showWindow && activation.Reason == ActivationFailure.NotRecorded
            && Window.GetWindow(this) is { } owner)
        {
            owner.Show();
            owner.Activate();
        }

        ResumeTimerNow();
    }

    /// <summary>
    /// 应用没开（或切不过去）时的循环提示。
    /// 用户每次点「我已打开，继续专注」都会重新检测一遍，直到成功或者他放弃。
    /// 「直接继续计时」是无条件出口：不管激活成没成功，都先把这一轮恢复起来，
    /// 决不能让用户被一个提示框困在暂停状态里。
    /// </summary>
    private void AskUserToOpenApplication(string message)
    {
        var owner = VisibleOwner;

        while (true)
        {
            var dialog = new AppNotRunningWindow(message) { Owner = owner };

            dialog.ShowDialog();

            if (dialog.ForceResume)
            {
                ResumeTimerNow();

                return;
            }

            if (!dialog.RetryRequested)
                return;

            var activation = ApplicationActivator.Activate(
                _viewModel.CurrentApplicationProcessName,
                _viewModel.CurrentApplicationName);

            if (activation.Success)
            {
                ResumeTimerNow();

                return;
            }

            message = activation.Reason == ActivationFailure.CannotBringToFront
                ? activation.Message
                : $"暂时没有检测到 {_viewModel.CurrentApplicationName}，请确认应用已经打开。\n\n{activation.Message}";
        }
    }

    /// <summary>真的把计时恢复起来（激活确认之后才调）。</summary>
    private void ResumeTimerNow()
    {
        try
        {
            _viewModel.ResumeSession();

            StartFocusGuard();
            StartCheckpointTimer();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // ==================== 进度检查点 ====================

    private void StartCheckpointTimer() => _checkpointTimer.Start();

    private void StopCheckpointTimer() => _checkpointTimer.Stop();

    /// <summary>把当前进度写进数据库。失败不打扰用户，下一轮还会再写。</summary>
    private void SaveCheckpoint()
    {
        if (!_viewModel.IsRunning && !_viewModel.IsPaused)
        {
            StopCheckpointTimer();

            return;
        }

        try
        {
            AppServices.Current?.Sessions.SaveCheckpoint();
        }
        catch (Exception)
        {
            // 写检查点是"尽力而为"的事：数据库忙就下一轮再写，不能弹窗打断专注。
        }
    }

    /// <summary>
    /// 退出程序之前存一次，免得最后几十秒白专注。
    /// </summary>
    public void SaveCheckpointBeforeExit()
    {
        StopCheckpointTimer();

        SaveCheckpoint();
    }

    // ==================== 给系统托盘用 ====================

    /// <summary>托盘需要的信息：现在是空闲、专注中还是暂停中，以及悬停提示写什么。</summary>
    public TraySnapshot CreateTraySnapshot()
    {
        var state = _viewModel.IsPaused
            ? TrayState.Paused
            : _viewModel.IsRunning
                ? TrayState.Running
                : TrayState.Idle;

        var toolTip = state switch
        {
            TrayState.Running => _viewModel.CurrentProjectName + " · 本轮 " + _viewModel.ElapsedText,
            TrayState.Paused => _viewModel.CurrentProjectName + " · 已暂停（本轮 " + _viewModel.ElapsedText + "）",
            _ => "Project Focus Manager · 空闲",
        };

        return new TraySnapshot(state, toolTip);
    }

    /// <summary>托盘菜单里的「暂停专注 / 继续专注」。</summary>
    public void TrayTogglePause()
    {
        if (_viewModel.IsRunning)
        {
            PauseWithReason();
            return;
        }

        if (_viewModel.IsPaused)
        {
            ResumeFromReminder();
        }
    }

    /// <summary>托盘菜单里的「结束本次专注」。</summary>
    public void TrayFinish() => FinishCurrentSession();

    /// <summary>
    /// 弹窗的 Owner。主窗口被隐藏到托盘时返回 null——
    /// 否则 WPF 会把这个窗口也一起藏起来。
    /// </summary>
    private Window? VisibleOwner
    {
        get
        {
            var owner = Window.GetWindow(this);

            return owner is { IsVisible: true } ? owner : null;
        }
    }

    private void ShowError(string message)
        => AppMessage.Show(VisibleOwner, message, "提示", MessageKind.Warning);
}
