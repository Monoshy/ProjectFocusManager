using System.Collections.ObjectModel;
using System.Windows.Threading;
using FocusManager.App.Mvvm;
using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Core.Time;

namespace FocusManager.App.ViewModels;

/// <summary>
/// 专注页面的「大脑」：管状态、管数据、管计时显示。
///
/// 界面（FocusView）只负责把这里的东西画出来，
/// 所有判断逻辑都放在这里，方便单独测试和修改。
/// </summary>
public sealed class FocusViewModel : ObservableObject
{
    /// <summary>界面刷新间隔。只影响「数字多久变一次」，不影响计时准不准。</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    private readonly AppServices _services;
    private readonly DispatcherTimer _ticker;

    private Project? _selectedProject;
    private ApplicationInfo? _selectedApplication;
    private Project? _activeProject;
    private ApplicationInfo? _activeApplication;
    private bool _displayActive = true;

    public FocusViewModel(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        _ticker = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval,
        };

        _ticker.Tick += (_, _) => RefreshDisplay();

        ReloadProjects();
        ReloadApplications();
        RefreshDisplay();
    }

    // ==================== 列表 ====================

    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<ApplicationInfo> Applications { get; } = [];

    public Project? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(SelectedProjectTotalText));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public ApplicationInfo? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    // ==================== 状态 ====================

    public bool IsIdle => !_services.Sessions.HasActiveSession;

    public bool IsRunning => _services.Sessions.Timer?.IsRunning == true;

    public bool IsPaused => _services.Sessions.Timer?.IsPaused == true;

    public bool CanStart => IsIdle && SelectedProject is not null;

    public string StateText => IsRunning
        ? "正在专注"
        : IsPaused
            ? "已暂停"
            : "还没开始";

    public string ElapsedText
        => TimeFormatter.ToClock(_services.Sessions.Timer?.EffectiveDuration ?? TimeSpan.Zero);

    public string PausedText
    {
        get
        {
            var timer = _services.Sessions.Timer;

            if (timer is null)
                return string.Empty;

            // 正在暂停：显示这一次已经停了多久（这一段还没写进库，要自己算）。
            if (timer.IsPaused && timer.CurrentPauseStartedAtUtc is { } startedAt)
            {
                var current = DateTimeOffset.UtcNow - startedAt;

                if (current < TimeSpan.Zero)
                    current = TimeSpan.Zero;

                return "已暂停 " + TimeFormatter.ToChinese(current);
            }

            // 计时中：显示这一轮累计暂停了多久。
            var total = timer.PausedDuration;

            return total <= TimeSpan.Zero ? string.Empty : "本轮累计暂停 " + TimeFormatter.ToChinese(total);
        }
    }

    public string CurrentProjectName => _activeProject?.Name ?? "—";

    public string CurrentApplicationName => _activeApplication?.Name ?? "未指定";

    /// <summary>本轮主要应用的进程名，用来判断前台窗口算不算在学习环境里。</summary>
    public string? CurrentApplicationProcessName => _activeApplication?.ProcessName;

    public string SelectedProjectTotalText
    {
        get
        {
            if (SelectedProject is null) return string.Empty;

            var seconds = _services.Projects.GetTotalEffectiveSeconds(SelectedProject.Id);

            return "这个项目累计专注 " + TimeFormatter.ToChinese(TimeSpan.FromSeconds(seconds));
        }
    }

    /// <summary>正在进行的这一轮所属项目的累计时间。</summary>
    public string ActiveProjectTotalText
    {
        get
        {
            if (_activeProject is null) return string.Empty;

            var seconds = _services.Projects.GetTotalEffectiveSeconds(_activeProject.Id);

            return "这个项目累计专注 " + TimeFormatter.ToChinese(TimeSpan.FromSeconds(seconds));
        }
    }

    public bool HasNoProjects => Projects.Count == 0;

    // ==================== 操作 ====================

    public void ReloadProjects()
    {
        var previousId = SelectedProject?.Id;

        Projects.Clear();

        foreach (var project in _services.Projects.List())
        {
            Projects.Add(project);
        }

        SelectedProject = Projects.FirstOrDefault(p => p.Id == previousId) ?? Projects.FirstOrDefault();

        OnPropertyChanged(nameof(HasNoProjects));
        OnPropertyChanged(nameof(SelectedProjectTotalText));
    }

    public void ReloadApplications()
    {
        var previousId = SelectedApplication?.Id;

        Applications.Clear();

        foreach (var application in _services.Applications.List())
        {
            Applications.Add(application);
        }

        SelectedApplication = Applications.FirstOrDefault(a => a.Id == previousId) ?? Applications.FirstOrDefault();
    }

    /// <summary>开始一轮专注。失败时抛异常，异常消息可以直接显示给用户。</summary>
    public void StartSession()
    {
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("请先选择一个项目。");
        }

        _services.Sessions.BeginSession(SelectedProject.Id, SelectedApplication?.Id);

        _activeProject = SelectedProject;
        _activeApplication = SelectedApplication;

        UpdateTicker();
        RaiseStateChanged();
    }

    public void PauseSession(string reason)
    {
        _services.Sessions.PauseSession(reason);
        RaiseStateChanged();
    }

    public void ResumeSession()
    {
        _services.Sessions.ResumeSession();
        RaiseStateChanged();
    }

    /// <summary>弹出总结窗口前调用，把计时先停住（写总结的时间不算专注时间）。</summary>
    public void FreezeForSummary()
    {
        _services.Sessions.FreezeTimerForSummary();
        RefreshDisplay();
    }

    /// <summary>用户在总结窗口里点了「继续专注」，恢复计时。</summary>
    public void ResumeAfterSummary()
    {
        _services.Sessions.UnfreezeTimerAfterSummary();
        RaiseStateChanged();
    }

    /// <summary>结束本轮专注并保存总结。返回保存后的会话记录，用于显示总结页。</summary>
    public FocusSession FinishSession(string progressNote, string? reflectionNote)
    {
        if (!_services.Sessions.HasActiveSession)
        {
            throw new InvalidOperationException("当前没有正在进行的专注。");
        }

        var session = _services.Sessions.CompleteSession(progressNote, reflectionNote);

        _activeProject = null;
        _activeApplication = null;

        ReloadProjects();
        RaiseStateChanged();

        return session;
    }

    /// <summary>
    /// 启动时从「上次没结束的那一轮」恢复之后，把整个页面刷新一遍。
    /// 这时计时器是新建的，所以项目名、应用名和计时状态都要重新读。
    /// </summary>
    public void RefreshAfterRecovery()
    {
        SyncActiveFromSession();
        ReloadProjects();
        ReloadApplications();
        RaiseStateChanged();
    }

    /// <summary>
    /// 从数据库里把「当前这一轮是哪个项目、哪个软件」读回来。
    /// 恢复上次没结束的专注之后必须调一次，否则页面上会显示成「—」。
    /// </summary>
    private void SyncActiveFromSession()
    {
        var projectId = _services.Sessions.CurrentProjectId;

        _activeProject = projectId is null ? null : _services.Projects.FindById(projectId.Value);

        long? applicationId = _services.Sessions.CurrentSessionId is { } sessionId
            ? _services.Sessions.GetSession(sessionId)?.PrimaryApplicationId
            : null;

        _activeApplication = applicationId is null ? null : _services.Applications.FindById(applicationId.Value);

        if (_activeProject is not null)
            SelectedProject = Projects.FirstOrDefault(p => p.Id == _activeProject.Id) ?? SelectedProject;

        if (_activeApplication is not null)
            SelectedApplication = Applications.FirstOrDefault(a => a.Id == _activeApplication.Id) ?? SelectedApplication;
    }

    /// <summary>界面每次刷新时调用，只更新显示用的文字。</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(PausedText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanStart));
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(CurrentProjectName));
        OnPropertyChanged(nameof(CurrentApplicationName));
        OnPropertyChanged(nameof(CurrentApplicationProcessName));
        OnPropertyChanged(nameof(ActiveProjectTotalText));
        UpdateTicker();
        RefreshDisplay();
    }

    /// <summary>
    /// 页面看不见的时候（比如切到了别的标签页），就不要每 200 毫秒刷新了。
    /// 计时本身不受影响，它靠的是单调时钟，不是这个刷新。
    /// </summary>
    public void SetDisplayActive(bool active)
    {
        if (_displayActive == active) return;

        _displayActive = active;
        UpdateTicker();
        RefreshDisplay();
    }

    private void UpdateTicker()
    {
        var timer = _services.Sessions.Timer;

        var shouldTick = _displayActive && (timer?.IsRunning == true || timer?.IsPaused == true);

        if (shouldTick)
        {
            if (!_ticker.IsEnabled) _ticker.Start();
        }
        else
        {
            _ticker.Stop();
        }
    }
}
