using System.Windows;
using FocusManager.App.Native;

namespace FocusManager.App.Views;

/// <summary>
/// 「你似乎离开了当前学习环境」的提醒窗口。
///
/// 行为约定：
/// 1. 强制置顶并抢到键盘焦点——按用户要求，这个提醒必须被看见，不能躲在后面；
/// 2. 不阻塞程序其它部分：它仍然是一个普通窗口，用户关掉它计时照常走；
/// 3. 同一段离开只弹一次，由 FocusGuardPolicy 保证，不会反复打扰。
/// </summary>
public partial class LeftFocusWindow : Window
{
    public LeftFocusWindow(string? windowTitle = null)
    {
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            MessageText.Text = $"你切到了「{windowTitle}」，它不在本轮的专注环境里。计时还在继续，要不要暂停由你自己决定。";
        }

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        Loaded += (_, _) =>
        {
            MoveToCenterOfScreen();

            // 按用户要求：这个提醒必须跳到最前面并拿到键盘焦点。
            ForegroundHelper.ForceActivate(this);
        };
    }

    /// <summary>用户选择「返回项目」：关掉提醒，专注继续。</summary>
    public event EventHandler? ReturnRequested;

    /// <summary>用户选择「暂停本次专注」：由外面弹暂停原因窗口。</summary>
    public event EventHandler? PauseRequested;

    /// <summary>用户选择「结束本次专注」：由外面走总结流程。</summary>
    public event EventHandler? FinishRequested;

    /// <summary>用户点了「确认切换」：这一段离开已经处理过，不该再弹第二次。</summary>
    public event EventHandler? SwitchConfirmed;

    /// <summary>
    /// 用户点了「添加辅助程序」：把刚才切过去的那个程序算进本轮。
    /// 由外面弹选择器，并且默认选中这个程序。
    /// </summary>
    public event EventHandler? AddApplicationRequested;

    /// <summary>用户直接关掉了提醒窗口（等于不理会，专注继续）。</summary>
    public event EventHandler? Dismissed;

    private void OnReturnClick(object sender, RoutedEventArgs e)
    {
        ReturnRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnSwitchClick(object sender, RoutedEventArgs e)
    {
        FirstStepPanel.Visibility = Visibility.Collapsed;
        SecondStepPanel.Visibility = Visibility.Visible;
        TitleText.Text = "确认要切换出去吗？";
        MessageText.Text = "如果确实要离开，这一轮可以先暂停，也可以直接结束并记录已经专注的时间。";

        SwitchConfirmed?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        SecondStepPanel.Visibility = Visibility.Collapsed;
        FirstStepPanel.Visibility = Visibility.Visible;
        TitleText.Text = "你似乎离开了当前学习环境";
        MessageText.Text = "刚才那个窗口不在本轮的专注环境里。计时还在继续，要不要暂停由你自己决定。";
    }

    private void OnAddApplicationClick(object sender, RoutedEventArgs e)
    {
        AddApplicationRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        PauseRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        FinishRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dismissed?.Invoke(this, EventArgs.Empty);

        base.OnClosed(e);
    }

    /// <summary>出现在屏幕中间，但不去抢当前程序的焦点。</summary>
    private void MoveToCenterOfScreen()
    {
        var area = SystemParameters.WorkArea;

        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = area.Top + ((area.Height - ActualHeight) / 2);
    }
}
