using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusManager.App.ViewModels;
using FocusManager.App.Views.Dialogs;

namespace FocusManager.App.Views;

/// <summary>
/// 随手记页面：专注时想到的事先记下来，之后再处理。
/// </summary>
public partial class IdeasView : UserControl
{
    private readonly IdeasViewModel _viewModel;

    public IdeasView()
    {
        InitializeComponent();

        var services = AppServices.Current;
        if (services is null)
        {
            _viewModel = null!;
            return;
        }

        _viewModel = new IdeasViewModel(services);
        DataContext = _viewModel;
    }

    /// <summary>每次切到这一页时重新读一遍，保证看到的是最新数据。</summary>
    public void RefreshFromDatabase()
    {
        if (AppServices.Current is null) return;

        // 正在专注的时候，默认就记到当前项目上。
        var currentProjectId = AppServices.Current.Sessions.CurrentProjectId;
        _viewModel.ReloadProjects(currentProjectId);
    }

    private void OnAddIdeaClick(object sender, RoutedEventArgs e) => SaveIdea();

    private void OnIdeaInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveIdea();
            e.Handled = true;
        }
    }

    private void SaveIdea()
    {
        try
        {
            _viewModel.AddCurrent();
            IdeaInputBox.Focus();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AppMessage.Show(Window.GetWindow(this), ex.Message, "这条没记上", MessageKind.Warning);
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item) return;

        try
        {
            _viewModel.Toggle(item);
        }
        catch (InvalidOperationException ex)
        {
            AppMessage.Show(Window.GetWindow(this), ex.Message, "改不了这条", MessageKind.Warning);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is not { } item) return;

        var confirmed = AppMessage.Confirm(
            Window.GetWindow(this),
            $"确定要删掉这条想法吗？\n\n{item.Content}\n\n删掉之后无法恢复。",
            "删除想法",
            okText: "删除",
            cancelText: "不删了",
            kind: MessageKind.Error);

        if (!confirmed) return;

        try
        {
            _viewModel.Delete(item);
        }
        catch (InvalidOperationException ex)
        {
            AppMessage.Show(Window.GetWindow(this), ex.Message, "删不掉这条", MessageKind.Warning);
        }
    }

    private static IdeaListItem? GetItem(object sender)
        => (sender as FrameworkElement)?.DataContext as IdeaListItem;
}
