using System.Windows;
using FocusManager.App.Native;

namespace FocusManager.App.Views.Dialogs;

/// <summary>
/// 暂停超时之后想回来，但学习软件已经不在了。
///
/// 只做两件事：把情况说清楚，给用户一个「我打开了，再试一次」的按钮。
/// 它不会去启动任何程序——这一步必须由用户自己完成。
/// </summary>
public partial class AppNotRunningWindow : Window
{
    public AppNotRunningWindow(string message)
    {
        InitializeComponent();

        MessageText.Text = message;

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
    }

    /// <summary>用户点了「我已打开，继续专注」，外面要再检测一次。</summary>
    public bool RetryRequested { get; private set; }

    /// <summary>
    /// 用户点了「直接继续计时」：不再等激活成功，先把这一轮恢复起来。
    /// 有了这个出口，对话框永远不会变成死胡同——激活被 Windows 拦住多少次，用户都还能继续。
    /// </summary>
    public bool ForceResume { get; private set; }

    /// <summary>更新提示文字（重试还是没找到时用）。</summary>
    public void UpdateMessage(string message) => MessageText.Text = message;

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        RetryRequested = true;

        DialogResult = true;
    }

    private void OnForceResumeClick(object sender, RoutedEventArgs e)
    {
        ForceResume = true;

        DialogResult = true;
    }
}
