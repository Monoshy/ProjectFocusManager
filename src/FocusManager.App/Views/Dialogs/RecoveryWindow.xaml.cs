using System.Windows;
using FocusManager.App.Native;
using FocusManager.Core.Models;
using FocusManager.Core.Time;

namespace FocusManager.App.Views.Dialogs;

/// <summary>用户对「上次没结束的那一轮」做的选择。</summary>
public enum RecoveryChoice
{
    /// <summary>按已经保存的内容结束这一轮。</summary>
    Finish,

    /// <summary>从现在开始接着计时。</summary>
    Resume
}

/// <summary>
/// 启动时如果发现上次有没正常结束的专注，就用这个窗口问用户一句。
///
/// 只做一件事：把「已经保存了多少」如实告诉用户，然后让用户在
/// 「结束这一轮」和「继续这一轮」之间选。程序关闭期间的时间不会
/// 算进专注时间，这一点必须让用户看清楚。
/// </summary>
public partial class RecoveryWindow : Window
{
    public RecoveryWindow(string projectName, FocusSession session, int otherUnfinishedCount = 0)
    {
        InitializeComponent();

        ProjectNameRun.Text = projectName;
        SavedText.Text = TimeFormatter.ToChinese(session.EffectiveTime);
        StartedText.Text = TimeFormatter.ToLocalDateTimeText(session.StartedAtUtc.ToLocalTime().DateTime);

        if (otherUnfinishedCount > 0)
        {
            ExtraText.Text = $"另外还有 {otherUnfinishedCount} 轮更早的记录没有结束，会一起按已经保存的内容结束。";
            ExtraText.Visibility = Visibility.Visible;
        }

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);

        // 用户直接点右上角关闭，等同于「结束这一轮」——不能让会话一直挂着。
        Choice = RecoveryChoice.Finish;
    }

    /// <summary>用户的选择。默认是结束，避免会话一直悬着。</summary>
    public RecoveryChoice Choice { get; private set; }

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        Choice = RecoveryChoice.Resume;
        DialogResult = true;
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        Choice = RecoveryChoice.Finish;
        DialogResult = true;
    }
}
