using System.Windows;
using System.Windows.Controls;
using FocusManager.App.ViewModels;
using FocusManager.App.Views.Dialogs;

namespace FocusManager.App.Views;

/// <summary>
/// 应用管理页：登记、修改、停用、删除学习软件。
/// </summary>
public partial class ApplicationsView : UserControl
{
    private readonly ApplicationsViewModel? _viewModel;

    public ApplicationsView()
    {
        InitializeComponent();

        var services = AppServices.Current;

        if (services is null) return;

        _viewModel = new ApplicationsViewModel(services);

        DataContext = _viewModel;
    }

    /// <summary>别的页面切换过来时，重新读一遍（用户可能在别处改过）。</summary>
    public void RefreshFromDatabase() => _viewModel?.Reload();

    private void OnAddFromFileClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var dialog = new ApplicationEditWindow("添加学习软件") { Owner = Window.GetWindow(this) };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _viewModel.AddFromFile(dialog.ApplicationName, dialog.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AppMessage.Show(Window.GetWindow(this), ex.Message, "没有添加成功", MessageKind.Warning);
        }
    }

    private void OnAddFromRunningClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var owner = Window.GetWindow(this);

        var picker = new WindowPickerWindow { Owner = owner };

        if (picker.ShowDialog() != true) return;

        var window = picker.SelectedWindow;

        if (window is null) return;

        var dialog = new ApplicationEditWindow(
            "添加学习软件",
            name: window.ProcessName,
            processName: window.ProcessName,
            executablePath: window.ExecutablePath ?? string.Empty)
        {
            Owner = owner
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _viewModel.AddFromRunningProcess(dialog.ApplicationName, dialog.ProcessName, dialog.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AppMessage.Show(owner, ex.Message, "没有添加成功", MessageKind.Warning);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        if (GetItem(sender) is not { } item) return;

        var dialog = new ApplicationEditWindow(
            "修改学习软件",
            name: item.Name,
            processName: item.Application.ProcessName,
            executablePath: item.Application.ExecutablePath,
            isEdit: true)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;

        _viewModel.Selected = item;

        try
        {
            _viewModel.UpdateSelected(dialog.ApplicationName, dialog.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AppMessage.Show(Window.GetWindow(this), ex.Message, "没有保存成功", MessageKind.Warning);
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        if (GetItem(sender) is not { } item) return;

        _viewModel.Selected = item;

        try
        {
            _viewModel.ToggleSelected();
        }
        catch (InvalidOperationException ex)
        {
            AppMessage.Show(Window.GetWindow(this), ex.Message, "操作没有完成", MessageKind.Warning);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        if (GetItem(sender) is not { } item) return;

        var owner = Window.GetWindow(this);

        var confirmed = AppMessage.Confirm(
            owner,
            $"确定要把「{item.Name}」从应用列表里删掉吗？\n\n" +
            "只会删掉这条登记，不会删除这个程序本身，也不会影响已经保存的专注记录。",
            "删除应用",
            okText: "删除",
            cancelText: "不删了",
            kind: MessageKind.Error);

        if (!confirmed) return;

        _viewModel.Selected = item;

        try
        {
            _viewModel.DeleteSelected();
        }
        catch (InvalidOperationException ex)
        {
            AppMessage.Show(owner, ex.Message, "操作没有完成", MessageKind.Warning);
        }
    }

    private static ApplicationListItem? GetItem(object sender)
        => (sender as FrameworkElement)?.DataContext as ApplicationListItem;
}
