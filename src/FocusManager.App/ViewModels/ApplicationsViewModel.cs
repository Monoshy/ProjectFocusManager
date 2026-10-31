using System.Collections.ObjectModel;
using FocusManager.App.Mvvm;
using FocusManager.Core.Models;
using FocusManager.Core.Services;

namespace FocusManager.App.ViewModels;

/// <summary>
/// 应用列表里的一行。
/// 这个类只负责「怎么显示」，真正的增删改在 <see cref="ApplicationService"/> 里。
/// </summary>
public sealed class ApplicationListItem
{
    public ApplicationListItem(ApplicationInfo application) => Application = application;

    public ApplicationInfo Application { get; }

    public string Name => Application.Name;

    public string ProcessText => string.IsNullOrWhiteSpace(Application.ProcessName)
        ? "还没识别到进程名"
        : "进程名 " + Application.ProcessName;

    public string PathText => Application.ExecutablePath;

    /// <summary>有没有登记程序文件路径。没有的话就不用显示那一行了。</summary>
    public bool HasPath => !string.IsNullOrWhiteSpace(Application.ExecutablePath);

    public bool IsEnabled => Application.IsEnabled;

    public string ToggleText => IsEnabled ? "停用" : "启用";

    /// <summary>填了程序路径，但那个文件已经不在了（被移动或删掉）。</summary>
    public bool IsPathBroken => Application.ExecutablePath.Length > 0
                                && !ApplicationService.HasUsablePath(Application);

    public string StatusText
    {
        get
        {
            if (!IsEnabled) return "已停用";
            if (IsPathBroken) return "程序文件找不到了";
            if (string.IsNullOrWhiteSpace(Application.ExecutablePath)) return "按进程名识别";

            return "正常";
        }
    }
}

/// <summary>应用管理页的数据和操作。</summary>
public sealed class ApplicationsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private ApplicationListItem? _selected;

    public ApplicationsViewModel(AppServices services)
    {
        _services = services;

        Reload();
    }

    public ObservableCollection<ApplicationListItem> Applications { get; } = [];

    public ApplicationListItem? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool HasApplications => Applications.Count > 0;

    public bool HasNoApplications => Applications.Count == 0;

    /// <summary>重新从数据库读一遍，并尽量保持原来选中的那一行。</summary>
    public void Reload()
    {
        var previousId = Selected?.Application.Id;

        Applications.Clear();

        foreach (var application in _services.Applications.List(includeDisabled: true))
        {
            Applications.Add(new ApplicationListItem(application));
        }

        Selected = Applications.FirstOrDefault(item => item.Application.Id == previousId)
                   ?? Applications.FirstOrDefault();

        OnPropertyChanged(nameof(HasApplications));
        OnPropertyChanged(nameof(HasNoApplications));
    }

    public ApplicationListItem AddFromFile(string name, string executablePath)
    {
        var added = _services.Applications.Add(name, executablePath);

        Reload();

        return SelectById(added.Id);
    }

    public ApplicationListItem AddFromRunningProcess(string name, string processName, string? executablePath)
    {
        var added = _services.Applications.AddFromRunningProcess(name, processName, executablePath);

        Reload();

        return SelectById(added.Id);
    }

    public void UpdateSelected(string name, string executablePath)
    {
        var item = Selected ?? throw new InvalidOperationException("请先在列表里选一个应用。");

        _services.Applications.Update(item.Application.Id, name, executablePath);

        Reload();
    }

    public void ToggleSelected()
    {
        var item = Selected ?? throw new InvalidOperationException("请先在列表里选一个应用。");

        _services.Applications.SetEnabled(item.Application.Id, !item.IsEnabled);

        Reload();
    }

    public void DeleteSelected()
    {
        var item = Selected ?? throw new InvalidOperationException("请先在列表里选一个应用。");

        _services.Applications.Delete(item.Application.Id);

        Selected = null;

        Reload();
    }

    private ApplicationListItem SelectById(long applicationId)
    {
        Selected = Applications.FirstOrDefault(item => item.Application.Id == applicationId);

        return Selected ?? throw new InvalidOperationException("添加之后读取列表失败。");
    }
}
