using System.Windows;
using System.Windows.Controls;
using FocusManager.App.ViewModels;
using FocusManager.App.Views.Dialogs;

namespace FocusManager.App.Views;

public partial class ProjectsView : UserControl
{
    private readonly ProjectsViewModel _viewModel;

    public ProjectsView()
    {
        InitializeComponent();

        var services = AppServices.Current
            ?? throw new InvalidOperationException("程序服务还没有初始化。");

        _viewModel = new ProjectsViewModel(services);
        DataContext = _viewModel;
    }

    /// <summary>每次切回这个页面时重新读数据库，保证看到的是最新的。</summary>
    public void RefreshFromDatabase() => _viewModel.Reload();

    /// <summary>
    /// 删除项目。删除会连带删掉这个项目的所有专注记录和想法，所以先把
    /// 「会删掉什么」如实摆出来，用户确认之后才真正删。
    /// </summary>
    private void OnDeleteProjectClick(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);

        var impact = _viewModel.GetSelectedDeletionImpact();
        if (impact is null)
            return;

        var details = impact.HasRecords
            ? $"会一起删掉：\n" +
              $"· {impact.SessionCount} 轮专注记录（累计 {impact.TotalEffectiveText}）\n" +
              $"· {impact.IdeaNoteCount} 条随手记想法\n\n" +
              "删掉之后无法恢复，也不会进回收站。"
            : "这个项目还没有任何专注记录，删掉不会丢失学习数据。";

        var confirmed = AppMessage.Confirm(
            owner,
            $"确定要删除「{impact.ProjectName}」吗？\n\n{details}",
            "删除项目",
            okText: "删除",
            cancelText: "不删了",
            kind: MessageKind.Error);

        if (!confirmed)
            return;

        try
        {
            var deleted = _viewModel.DeleteSelectedProject();

            AppMessage.Show(
                owner,
                $"「{deleted.ProjectName}」已经删掉了。" +
                (deleted.HasRecords ? $"\n同时清掉了 {deleted.SessionCount} 轮专注记录。" : string.Empty),
                "项目已删除",
                MessageKind.Success);
        }
        catch (InvalidOperationException ex)
        {
            AppMessage.Show(owner, ex.Message, "这个项目暂时不能删", MessageKind.Warning);
        }
    }

    /// <summary>
    /// 删掉一条专注记录。只影响这一次，项目本身和别的记录都还在。
    /// 项目累计时间会跟着减少，所以先把「会少多少」说清楚。
    /// </summary>
    private void OnDeleteSessionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionListItem item)
            return;

        var owner = Window.GetWindow(this);

        var confirmed = AppMessage.Confirm(
            owner,
            $"确定要删掉 {item.DateText} 这一条吗？\n\n"
            + $"这一轮记的是 {item.DurationText}，删掉之后项目累计时间会相应减少。撤不回来。",
            "删除这条记录",
            okText: "删除",
            cancelText: "不删了",
            kind: MessageKind.Error);

        if (!confirmed)
            return;

        try
        {
            var impact = _viewModel.DeleteSession(item.Session.Id);

            AppMessage.Show(
                owner,
                $"这条记录已经删掉了，{impact.EffectiveText} 从项目累计里减掉了。",
                "记录已删除",
                MessageKind.Success);
        }
        catch (InvalidOperationException ex)
        {
            AppMessage.Show(owner, ex.Message, "这条记录暂时不能删", MessageKind.Warning);
        }
    }
}
