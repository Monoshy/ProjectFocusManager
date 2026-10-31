using System.Windows;
using FocusManager.App.Native;
using FocusManager.Core.Time;

namespace FocusManager.App.Views.Dialogs;

/// <summary>
/// 结束一轮专注时弹出的总结窗口。
/// 它不负责保存数据，只负责「显示数字 + 收集用户写的内容」。
/// 保存由调用方交给 SessionService 完成。
/// </summary>
public partial class SessionSummaryWindow : Window
{
    public SessionSummaryWindow(
        string projectName,
        TimeSpan effectiveDuration,
        TimeSpan pausedDuration,
        int pauseCount,
        int ideaCount)
    {
        InitializeComponent();

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);

        EffectiveDuration = effectiveDuration;

        ProjectNameRun.Text = string.IsNullOrWhiteSpace(projectName) ? "—" : projectName;
        EffectiveText.Text = TimeFormatter.ToChinese(effectiveDuration);
        PausedText.Text = pausedDuration <= TimeSpan.Zero ? "没有" : TimeFormatter.ToChinese(pausedDuration);
        CountText.Text = $"{pauseCount} 次 / {ideaCount} 条";

        Loaded += (_, _) => ProgressBox.Focus();
    }

    /// <summary>本轮的有效专注时间，供调用方显示提示。</summary>
    public TimeSpan EffectiveDuration { get; }

    public string ProgressNote => ProgressBox.Text.Trim();

    public string? ReflectionNote
        => string.IsNullOrWhiteSpace(ReflectionBox.Text) ? null : ReflectionBox.Text.Trim();

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProgressBox.Text))
        {
            ErrorText.Text = "请先写一句「本轮完成了什么」，这是唯一必须填的内容。";
            ErrorText.Visibility = Visibility.Visible;

            ProgressBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
