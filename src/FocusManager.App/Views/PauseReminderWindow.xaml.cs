using System.Windows;
using FocusManager.App.Native;
using FocusManager.Core.Time;

namespace FocusManager.App.Views;

/// <summary>
/// 暂停满 5 分钟（或用户设置的分钟数）之后弹出的提醒。
///
/// 行为约定（对应需求 4.4）：
/// 1. 只弹一次，不重复催；用户关掉它也不会再弹第二次；
/// 2. 不自动恢复计时、也不自动结束这一轮，等用户自己决定；
/// 3. 不抢键盘焦点（ShowActivated=False），但要浮在最上面，免得被别的窗口盖住；
/// 4. 用户一直不处理，它就一直在那儿。
/// </summary>
public partial class PauseReminderWindow : Window
{
    public PauseReminderWindow(string projectName, TimeSpan paused)
    {
        InitializeComponent();

        MessageText.Text =
            $"「{projectName}」已经暂停 {TimeFormatter.ToChinese(paused)}。"
            + "想继续就点「回到项目」；这一轮不想继续了，就点「结束本次专注」，"
            + "已经专注的时间会保留下来。";

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        Loaded += (_, _) => MoveToCenterOfScreen();
    }

    /// <summary>用户选择回到项目（继续这一轮）。</summary>
    public event EventHandler? ResumeRequested;

    /// <summary>用户选择结束这一轮。</summary>
    public event EventHandler? FinishRequested;

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        ResumeRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        FinishRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void MoveToCenterOfScreen()
    {
        var area = SystemParameters.WorkArea;

        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = area.Top + ((area.Height - ActualHeight) / 2);
    }
}
