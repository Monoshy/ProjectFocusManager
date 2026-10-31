using System.Windows;
using FocusManager.App.Native;

namespace FocusManager.App.Views.Dialogs;

public partial class PauseReasonWindow : Window
{
    public PauseReasonWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        ReasonWater.IsChecked = true;

        Loaded += (_, _) => ReasonWater.Focus();
    }

    /// <summary>用户选择的暂停原因。</summary>
    public string Reason { get; private set; } = string.Empty;

    private void OnPresetChecked(object sender, RoutedEventArgs e)
    {
        if (CustomReasonBox is null) return;

        CustomReasonBox.Visibility = Visibility.Collapsed;
        HideError();
    }

    private void OnOtherChecked(object sender, RoutedEventArgs e)
    {
        if (CustomReasonBox is null) return;

        CustomReasonBox.Visibility = Visibility.Visible;
        CustomReasonBox.Focus();
        HideError();
    }

    private void OnCustomReasonChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => HideError();

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (ReasonOther.IsChecked == true)
        {
            var custom = CustomReasonBox.Text.Trim();

            if (custom.Length == 0)
            {
                ShowError("选了「其他」的话，请写一句具体原因。");

                CustomReasonBox.Focus();
                return;
            }

            Reason = custom;
        }
        else
        {
            Reason = SelectedPresetText();
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private string SelectedPresetText()
    {
        if (ReasonErrand.IsChecked == true) return "临时有事";
        if (ReasonLookup.IsChecked == true) return "查找资料";

        return "喝水";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
