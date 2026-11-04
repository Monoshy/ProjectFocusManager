using System.Collections.ObjectModel;
using FocusManager.App.Mvvm;
using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Core.Time;

namespace FocusManager.App.ViewModels;

/// <summary>项目列表里的一行。</summary>
public sealed class ProjectListItem
{
    public ProjectListItem(Project project, long totalSeconds)
    {
        Project = project;
        TotalSeconds = totalSeconds;
    }

    public Project Project { get; }

    public long TotalSeconds { get; }

    public string Name => Project.Name;

    public string TotalText => "累计 " + TimeFormatter.ToChinese(TimeSpan.FromSeconds(TotalSeconds));

    public override string ToString() => Name;
}

/// <summary>「按天统计」里的一行。</summary>
public sealed class DailyTotalItem
{
    public DailyTotalItem(DateOnly date, long effectiveSeconds, int sessionCount)
    {
        Date = date;
        EffectiveSeconds = effectiveSeconds;
        SessionCount = sessionCount;
    }

    public DateOnly Date { get; }

    public long EffectiveSeconds { get; }

    public int SessionCount { get; }

    public string DateText => TimeFormatter.ToLocalDateText(Date.ToDateTime(TimeOnly.MinValue));

    public string DurationText => TimeFormatter.ToChinese(TimeSpan.FromSeconds(EffectiveSeconds));

    public string SessionCountText => SessionCount <= 1 ? string.Empty : $"（{SessionCount} 次）";
}

/// <summary>「每次专注的记录」里的一条。</summary>
public sealed class SessionListItem
{
    public SessionListItem(FocusSession session)
    {
        Session = session;
    }

    public FocusSession Session { get; }

    public string DateText
        => TimeFormatter.ToLocalDateTimeText(Session.StartedAtUtc.ToLocalTime().DateTime);

    public string DurationText => TimeFormatter.ToChinese(Session.EffectiveTime);

    public string PausedText
        => Session.PausedSeconds <= 0 ? string.Empty : "暂停 " + TimeFormatter.ToChinese(Session.PausedTime);

    public string ProgressText => string.IsNullOrWhiteSpace(Session.ProgressNote)
        ? "（这一轮没有写进度说明）"
        : Session.ProgressNote;

    public string ReflectionText => Session.ReflectionNote ?? string.Empty;

    public bool HasReflection => !string.IsNullOrWhiteSpace(Session.ReflectionNote);
}

/// <summary>
/// 项目记录页面的「大脑」：左边选项目，右边看这个项目的历史。
/// </summary>
public sealed class ProjectsViewModel : ObservableObject
{
    private readonly AppServices _services;

    private ProjectListItem? _selectedProject;
    private string _summaryText = "还没有选择项目。";

    public ProjectsViewModel(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        Reload();
    }

    public ObservableCollection<ProjectListItem> Projects { get; } = [];

    public ObservableCollection<DailyTotalItem> DailyTotals { get; } = [];

    public ObservableCollection<SessionListItem> Sessions { get; } = [];

    public ProjectListItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                LoadDetails();
            }
        }
    }

    /// <summary>有没有选中项目（决定「删除这个项目」按钮能不能点）。</summary>
    public bool HasSelection => SelectedProject is not null;

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool HasProjects => Projects.Count > 0;

    public bool HasNoSessions => Sessions.Count == 0;

    /// <summary>
    /// 删除前先看一眼会连带删掉什么。只读，不改数据。
    /// 没选项目时返回 null。
    /// </summary>
    public ProjectDeletionImpact? GetSelectedDeletionImpact()
        => SelectedProject is null ? null : _services.Projects.GetDeletionImpact(SelectedProject.Project.Id);

    /// <summary>
    /// 删除选中的项目，连带它的所有专注记录和想法，然后刷新列表。
    /// 还有一轮专注没结束时 ProjectService 会抛异常，调用方负责提示用户。
    /// </summary>
    public ProjectDeletionImpact DeleteSelectedProject()
    {
        if (SelectedProject is null)
            throw new InvalidOperationException("请先选中一个项目。");

        var impact = _services.Projects.Delete(SelectedProject.Project.Id);

        Reload();

        return impact;
    }

    /// <summary>
    /// 删掉一条专注记录（不动项目本身）。
    /// 这一轮如果还在进行中，SessionService 会抛异常，调用方负责提示用户。
    /// </summary>
    public SessionDeletionImpact DeleteSession(long sessionId)
    {
        var impact = _services.Sessions.DeleteSession(sessionId);

        Reload();

        return impact;
    }

    /// <summary>重新从数据库读一遍项目列表（结束一轮专注后需要调用）。</summary>
    public void Reload()
    {
        var previousId = SelectedProject?.Project.Id;

        Projects.Clear();

        foreach (var project in _services.Projects.List())
        {
            var total = _services.Projects.GetTotalEffectiveSeconds(project.Id);

            Projects.Add(new ProjectListItem(project, total));
        }

        OnPropertyChanged(nameof(HasProjects));

        SelectedProject = Projects.FirstOrDefault(p => p.Project.Id == previousId) ?? Projects.FirstOrDefault();

        if (SelectedProject is null)
        {
            LoadDetails();
        }
    }

    private void LoadDetails()
    {
        DailyTotals.Clear();
        Sessions.Clear();

        if (SelectedProject is null)
        {
            SummaryText = "还没有选择项目。";
            OnPropertyChanged(nameof(HasNoSessions));
            return;
        }

        var projectId = SelectedProject.Project.Id;

        var statistics = _services.Statistics.GetProjectStatistics(projectId);

        var lastText = statistics.LastSessionAtUtc is null
            ? "还没有专注记录"
            : "最近一次：" + TimeFormatter.ToLocalDateTimeText(
                statistics.LastSessionAtUtc.Value.ToLocalTime().DateTime);

        SummaryText = $"共 {statistics.SessionCount} 次专注，{lastText}。";

        foreach (var daily in _services.Statistics.GetDailyTotals(projectId))
        {
            DailyTotals.Add(new DailyTotalItem(daily.Date, daily.EffectiveSeconds, daily.SessionCount));
        }

        foreach (var session in _services.Statistics.GetSessions(projectId))
        {
            Sessions.Add(new SessionListItem(session));
        }

        OnPropertyChanged(nameof(HasNoSessions));
    }
}
