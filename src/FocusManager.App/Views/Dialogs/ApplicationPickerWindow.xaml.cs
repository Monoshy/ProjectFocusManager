using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusManager.App.Native;
using FocusManager.App.Services;
using FocusManager.Core.Models;

namespace FocusManager.App.Views.Dialogs;

/// <summary>选择器里的一行：一个已注册的程序，外加「是不是已经加过了」。</summary>
public sealed class ApplicationRow
{
    public ApplicationRow(ApplicationInfo application, bool alreadyAdded)
    {
        Application = application;
        AlreadyAdded = alreadyAdded;
    }

    public ApplicationInfo Application { get; }

    public bool AlreadyAdded { get; }

    public string Name => Application.Name;

    public string ProcessText => "进程名：" + Application.ProcessName;
}

/// <summary>
/// 给「辅助程序」挑一个已经注册的学习软件。
///
/// 为什么不直接复用应用管理页：这里的重点是「快」——一个项目可能要配好几个程序，
/// 而且要能在专注进行中随手补一个。所以这里带搜索、带「从正在运行的程序里挑」，
/// 并且能当场新建应用，不用先跑去别的页面。
/// </summary>
public partial class ApplicationPickerWindow : Window
{
    private readonly HashSet<long> _alreadyAdded;
    private readonly string? _preselectProcessName;

    public ApplicationPickerWindow(
        IEnumerable<long>? alreadyAddedIds = null,
        string? preselectProcessName = null)
    {
        InitializeComponent();

        _alreadyAdded = alreadyAddedIds is null ? [] : [.. alreadyAddedIds];
        _preselectProcessName = preselectProcessName;

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);

        Reload();

        Loaded += (_, _) =>
        {
            ApplyPreselect();
            ApplicationListBox.Focus();
        };
    }

    public ObservableCollection<ApplicationRow> Rows { get; } = [];

    public ApplicationInfo? SelectedApplication
        => (ApplicationListBox.SelectedItem as ApplicationRow)?.Application;

    /// <summary>重新读一遍已注册的应用，并套上当前的搜索词。</summary>
    private void Reload(long? selectId = null)
    {
        var services = AppServices.Current;
        if (services is null) return;

        var keyword = SearchBox?.Text?.Trim() ?? string.Empty;

        Rows.Clear();

        foreach (var application in services.Applications.List())
        {
            if (keyword.Length > 0
                && !application.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !application.ProcessName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Rows.Add(new ApplicationRow(application, _alreadyAdded.Contains(application.Id)));
        }

        ApplicationListBox.ItemsSource = Rows;

        if (selectId is { } id)
        {
            ApplicationListBox.SelectedItem = Rows.FirstOrDefault(r => r.Application.Id == id);
        }

        UpdateHint();
    }

    /// <summary>
    /// 默认选中当前正在用的那个程序。
    /// 从「离开提醒」里点进来时，传的是刚才那个窗口的进程名——
    /// 用户既然要把它加进来，就别再让他自己在一堆名字里翻。
    /// </summary>
    private void ApplyPreselect()
    {
        if (ApplicationListBox.SelectedItem is not null)
            return;

        if (!string.IsNullOrWhiteSpace(_preselectProcessName))
        {
            var normalized = FocusManager.Core.Services.FocusAllowList.Normalize(_preselectProcessName);

            var match = Rows.FirstOrDefault(r =>
                string.Equals(r.Application.ProcessName, normalized, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                ApplicationListBox.SelectedItem = match;
                ApplicationListBox.ScrollIntoView(match);
                UpdateHint();
                return;
            }

            // 没注册过：直接把这个进程名预填进「新建应用」，省一次查找。
            EmptyHintText.Text = $"「{_preselectProcessName}」还没有登记过，点下面的「新建应用…」就能把它加进来。";
            EmptyHintBorder.Visibility = Visibility.Visible;
        }

        if (Rows.Count > 0 && ApplicationListBox.SelectedItem is null)
        {
            // 默认落在第一个「还没加过」的行上：已经在本轮里的程序点进来也加不了，
            // 默认选中它只会让用户先看到一句「不用再加一次」。
            var firstUsable = Rows.FirstOrDefault(r => !r.AlreadyAdded);
            if (firstUsable is not null)
            {
                ApplicationListBox.SelectedItem = firstUsable;
                ApplicationListBox.ScrollIntoView(firstUsable);
                UpdateHint();
            }
        }
    }

    private void UpdateHint()
    {
        AddButton.IsEnabled = SelectedApplication is { } current && !_alreadyAdded.Contains(current.Id);

        if (Rows.Count == 0)
        {
            EmptyHintText.Text = string.IsNullOrWhiteSpace(SearchBox?.Text)
                ? "还没有登记过任何学习应用。点下面的「新建应用…」或者「从正在运行的程序里挑…」加一个。"
                : "没有找到匹配的程序，换个词试试。";
            EmptyHintBorder.Visibility = Visibility.Visible;
            return;
        }

        if (SelectedApplication is { } selected && _alreadyAdded.Contains(selected.Id))
        {
            EmptyHintText.Text = "这个程序已经在本轮里了（主程序或辅助程序），不用再加一次。";
            EmptyHintBorder.Visibility = Visibility.Visible;
            return;
        }

        EmptyHintBorder.Visibility = Visibility.Collapsed;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => Reload();

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHint();

    private void OnFromWindowClick(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;
        if (services is null) return;

        var picker = new WindowPickerWindow { Owner = this };

        if (picker.ShowDialog() != true || picker.SelectedWindow is not { } running)
            return;

        try
        {
            // 已经按进程名登记过的话，直接用那一条，不要再建一个重复的。
            var existing = services.Applications.List(includeDisabled: true)
                .FirstOrDefault(a => string.Equals(a.ProcessName, running.ProcessName, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                Reload(existing.Id);
                UpdateHint();
                return;
            }

            var edit = new ApplicationEditWindow(
                "添加学习软件",
                name: running.ProcessName,
                processName: running.ProcessName,
                executablePath: running.ExecutablePath ?? string.Empty)
            {
                Owner = this,
            };

            if (edit.ShowDialog() != true)
                return;

            var created = services.Applications.AddFromRunningProcess(
                edit.ApplicationName,
                running.ProcessName,
                edit.ExecutablePath.Length > 0 ? edit.ExecutablePath : null);

            Reload(created.Id);
            UpdateHint();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ShowHint(ex.Message);
        }
    }

    private void OnNewApplicationClick(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;
        if (services is null) return;

        var edit = new ApplicationEditWindow("添加学习软件") { Owner = this };

        if (edit.ShowDialog() != true)
            return;

        try
        {
            var created = services.Applications.Add(edit.ApplicationName, edit.ExecutablePath);

            Reload(created.Id);
            UpdateHint();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ShowHint(ex.Message);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (SelectedApplication is not { } selected)
        {
            ShowHint("请先在列表里选一个程序。");
            return;
        }

        if (_alreadyAdded.Contains(selected.Id))
        {
            ShowHint("这个程序已经在本轮里了（主程序或辅助程序），不用再加一次。");
            return;
        }

        DialogResult = true;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedApplication is not null)
            OnAddClick(sender, e);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowHint(string message)
    {
        EmptyHintText.Text = message;
        EmptyHintBorder.Visibility = Visibility.Visible;
    }
}
