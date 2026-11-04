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
        ReloadAuxiliaryApplications();
        RefreshDisplay();
    }

    // ==================== 列表 ====================

    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<ApplicationInfo> Applications { get; } = [];

    /// <summary>
    /// 本轮一起用的辅助程序。空闲时看的是「这个项目平时挂着哪些」，
    /// 专注中看的是「这一轮实际记下了哪些」，两者在开始的那一刻对齐。
    /// </summary>
    public ObservableCollection<ApplicationInfo> AuxiliaryApplications { get; } = [];

    public bool HasAuxiliaryApplications => AuxiliaryApplications.Count > 0;

    public string AuxiliarySummaryText => AuxiliaryApplications.Count == 0
        ? string.Empty
        : string.Join("、", AuxiliaryApplications.Select(a => a.Name));

    /// <summary>一个辅助程序都没有时的说明，只在空闲态显示。</summary>
    public string AuxiliaryEmptyHint
        => "还没有辅助程序。一个项目可以同时用好几个程序，比如主程序是 CCtalk，另外还要开 Obsidian 记笔记。";

    /// <summary>这一轮允许的辅助程序进程名，用来拼前台窗口的白名单。</summary>
    public IReadOnlyList<string> CurrentAuxiliaryProcessNames
        => AuxiliaryApplications
            .Select(a => a.ProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    public Project? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(SelectedProjectTotalText));
                OnPropertyChanged(nameof(CanStart));

                // 换了项目，跟着换一批辅助程序：每个项目平时用的程序不一样。
                if (IsIdle)
                    ReloadAuxiliaryApplications();
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

    /// <summary>
    /// 重新读一遍本轮/本项目的辅助程序。
    /// 专注中读的是这一轮的快照（改项目挂载不会影响已经在跑的这一轮），
    /// 空闲时读的是项目平时挂着的那几个。
    /// </summary>
    public void ReloadAuxiliaryApplications()
    {
        AuxiliaryApplications.Clear();

        if (!IsIdle && _services.Sessions.CurrentSessionId is { } sessionId)
        {
            foreach (var row in _services.Sessions.GetSessionApplications(sessionId))
            {
                if (row.IsPrimary) continue;

                var application = row.ApplicationId is { } id
                    ? _services.Applications.FindById(id)
                    : null;

                // 应用被删掉了也照样显示：这一轮确实用过它，名字得留着。
                AuxiliaryApplications.Add(application ?? new ApplicationInfo
                {
                    Id = row.ApplicationId ?? 0,
                    Name = row.NameSnapshot,
                    ProcessName = row.ProcessNameSnapshot,
                });
            }
        }
        else if (SelectedProject is { } project)
        {
            foreach (var application in _services.ProjectApplications.ListForProject(project.Id))
            {
                AuxiliaryApplications.Add(application);
            }
        }

        OnPropertyChanged(nameof(HasAuxiliaryApplications));
        OnPropertyChanged(nameof(AuxiliarySummaryText));
        OnPropertyChanged(nameof(AuxiliaryEmptyHint));
        OnPropertyChanged(nameof(CurrentAuxiliaryProcessNames));
    }

    /// <summary>
    /// 加一个辅助程序。空闲时挂到项目上（下次开始自动带上）；
    /// 专注中除了挂到项目上，还要立刻写进这一轮，让它马上算进白名单。
    /// </summary>
    public void AddAuxiliaryApplication(long applicationId)
    {
        if (!IsIdle)
        {
            _services.Sessions.AttachAuxiliaryApplication(applicationId);
            ReloadAuxiliaryApplications();
            return;
        }

        if (SelectedProject is null)
            throw new InvalidOperationException("请先选择一个项目。");

        _services.ProjectApplications.Add(SelectedProject.Id, applicationId);
        ReloadAuxiliaryApplications();
    }

    /// <summary>移除一个辅助程序：项目上摘掉，专注中同时从这一轮摘掉。</summary>
    public void RemoveAuxiliaryApplication(long applicationId)
    {
        if (!IsIdle)
        {
            _services.Sessions.DetachAuxiliaryApplication(applicationId);
        }
        else if (SelectedProject is { } project)
        {
            _services.ProjectApplications.Remove(project.Id, applicationId);
        }

        ReloadAuxiliaryApplications();
    }

    /// <summary>开始一轮专注。失败时抛异常，异常消息可以直接显示给用户。</summary>
    public void StartSession()
    {
        if (SelectedProject is null)
        {
            throw new InvalidOperationException("请先选择一个项目。");
        }

        _services.Sessions.BeginSession(
            SelectedProject.Id,
            SelectedApplication?.Id,
            AuxiliaryApplications.Select(a => a.Id).Where(id => id > 0).ToList());

        _activeProject = SelectedProject;
        _activeApplication = SelectedApplication;

        // 从这一刻起，辅助程序看的是「这一轮的快照」，而不是项目平时挂着的那几个。
        ReloadAuxiliaryApplications();

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
    /// 作废当前这一轮：不写记录、不涨项目累计时间，直接把这轮丢掉。
    /// 返回作废前已经累计的有效秒数，用来告诉用户丢了多少。
    /// </summary>
    public long DiscardSession()
    {
        if (!_services.Sessions.HasActiveSession)
        {
            throw new InvalidOperationException("当前没有正在进行的专注。");
        }

        var seconds = _services.Sessions.DiscardActiveSession();

        _activeProject = null;
        _activeApplication = null;

        ReloadProjects();
        ReloadAuxiliaryApplications();
        RaiseStateChanged();

        return seconds;
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
        ReloadAuxiliaryApplications();
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
