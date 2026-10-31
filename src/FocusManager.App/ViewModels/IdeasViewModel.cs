using System.Collections.ObjectModel;
using FocusManager.App.Mvvm;
using FocusManager.Core.Models;
using FocusManager.Core.Services;
using FocusManager.Core.Time;

namespace FocusManager.App.ViewModels;

/// <summary>列表里的一条想法，包一层只为了给界面提供现成的文字。</summary>
public sealed class IdeaListItem
{
    public IdeaListItem(IdeaNote note)
    {
        Note = note;
    }

    public IdeaNote Note { get; }

    public string Content => Note.Content;

    public bool IsDone => Note.Status == IdeaNoteStatus.Done;

    public string StatusText => IsDone ? "已完成" : "待处理";

    /// <summary>按钮上的字：当前是什么状态，点了就变成另一个状态。</summary>
    public string ToggleText => IsDone ? "标回待处理" : "标记完成";

    public string TimeText => TimeFormatter.ToLocalDateTimeText(Note.CreatedAtUtc);

    public string DoneAtText => Note.CompletedAtUtc is { } doneAt
        ? "完成于 " + TimeFormatter.ToLocalDateTimeText(doneAt)
        : string.Empty;

    public bool HasDoneAt => Note.CompletedAtUtc is not null;

    public bool FromFocusSession => Note.SessionId is not null;
}

/// <summary>
/// 随手记页面的状态：选项目、记一笔、按状态筛选、标记完成、删除。
/// </summary>
public sealed class IdeasViewModel : ObservableObject
{
    private static readonly string[] FilterNames = ["全部", "待处理", "已完成"];

    private readonly AppServices _services;
    private Project? _selectedProject;
    private string _newIdeaText = string.Empty;
    private int _selectedFilterIndex;

    public IdeasViewModel(AppServices services)
    {
        _services = services;
        ReloadProjects();
    }

    public ObservableCollection<Project> Projects { get; } = [];

    public ObservableCollection<IdeaListItem> Ideas { get; } = [];

    public IReadOnlyList<string> Filters => FilterNames;

    public Project? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanAdd));
                Reload();
            }
        }
    }

    /// <summary>输入框里的内容。</summary>
    public string NewIdeaText
    {
        get => _newIdeaText;
        set
        {
            if (SetProperty(ref _newIdeaText, value))
                OnPropertyChanged(nameof(CanAdd));
        }
    }

    public int SelectedFilterIndex
    {
        get => _selectedFilterIndex;
        set
        {
            if (SetProperty(ref _selectedFilterIndex, value))
                Reload();
        }
    }

    public bool HasSelection => SelectedProject is not null;

    public bool CanAdd => HasSelection && !string.IsNullOrWhiteSpace(NewIdeaText);

    public bool HasIdeas => Ideas.Count > 0;

    public bool HasNoIdeas => !HasIdeas;

    public bool HasProjects => Projects.Count > 0;

    public bool HasNoProjects => !HasProjects;

    /// <summary>顶部那句「共 N 条，其中 M 条还没处理」。</summary>
    public string SummaryText
    {
        get
        {
            if (SelectedProject is null)
                return string.Empty;

            var total = _services.Ideas.CountByProject(SelectedProject.Id);
            if (total == 0)
                return "还没有记过想法。";

            var pending = _services.Ideas.CountByProject(SelectedProject.Id, IdeaNoteStatus.Pending);
            return pending == 0
                ? $"共 {total} 条，都处理完了。"
                : $"共 {total} 条，其中 {pending} 条还没处理。";
        }
    }

    /// <summary>重新读项目列表，尽量保留当前选中的项目。</summary>
    public void ReloadProjects(long? preferProjectId = null)
    {
        var keepId = preferProjectId ?? SelectedProject?.Id;

        Projects.Clear();
        foreach (var project in _services.Projects.List())
            Projects.Add(project);

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(HasNoProjects));

        var next = keepId is null
            ? Projects.FirstOrDefault()
            : Projects.FirstOrDefault(p => p.Id == keepId) ?? Projects.FirstOrDefault();

        // 直接改字段再走一遍刷新，避免 setter 里的 Reload 被调用两次。
        _selectedProject = next;
        OnPropertyChanged(nameof(SelectedProject));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanAdd));

        Reload();
    }

    /// <summary>按当前项目和筛选条件重新读列表。</summary>
    public void Reload()
    {
        Ideas.Clear();

        if (SelectedProject is { } project)
        {
            IdeaNoteStatus? status = SelectedFilterIndex switch
            {
                1 => IdeaNoteStatus.Pending,
                2 => IdeaNoteStatus.Done,
                _ => null
            };

            foreach (var note in _services.Ideas.ListByProject(project.Id, status))
                Ideas.Add(new IdeaListItem(note));
        }

        OnPropertyChanged(nameof(HasIdeas));
        OnPropertyChanged(nameof(HasNoIdeas));
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>把输入框里的内容记下来。记成功后输入框会清空。</summary>
    public IdeaNote AddCurrent()
    {
        if (SelectedProject is not { } project)
            throw new InvalidOperationException("请先选择一个项目。");

        var content = NewIdeaText;
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("想法内容不能为空。", nameof(NewIdeaText));

        // 如果这个项目正好在专注中，就把想法挂到当前这一轮上，方便以后回顾。
        var sessionId = _services.Sessions.CurrentProjectId == project.Id
            ? _services.Sessions.CurrentSessionId
            : null;

        var note = _services.Ideas.Add(project.Id, content, sessionId);
        NewIdeaText = string.Empty;
        Reload();
        return note;
    }

    public void Toggle(IdeaListItem item)
    {
        _services.Ideas.ToggleStatus(item.Note.Id);
        Reload();
    }

    public void Delete(IdeaListItem item)
    {
        _services.Ideas.Delete(item.Note.Id);
        Reload();
    }
}
