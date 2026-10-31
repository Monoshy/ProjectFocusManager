using System.Windows;
using FocusManager.App.Native;
using FocusManager.Core.Services;

namespace FocusManager.App.Views.Dialogs;

/// <summary>
/// 点主窗口右上角关闭按钮时问一句「你想怎么处理」。
///
/// 这个窗口只负责问，不负责执行——真正的动作由 MainWindow 决定，
/// 免得「关闭」这件事在两个地方各写一套。
/// </summary>
public partial class CloseChoiceWindow : Window
{
    public CloseChoiceWindow(CloseBehavior current)
    {
        InitializeComponent();

        Choice = current == CloseBehavior.Exit ? CloseBehavior.Exit : CloseBehavior.MinimizeToTray;

        TrayOption.IsChecked = Choice == CloseBehavior.MinimizeToTray;
        ExitOption.IsChecked = Choice == CloseBehavior.Exit;

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
    }

    /// <summary>用户选了哪个。</summary>
    public CloseBehavior Choice { get; private set; }

    /// <summary>用户有没有勾「记住我的选择」。</summary>
    public bool Remember => RememberCheck.IsChecked == true;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Choice = ExitOption.IsChecked == true ? CloseBehavior.Exit : CloseBehavior.MinimizeToTray;

        DialogResult = true;
    }
}
