using System.Windows;
using FocusManager.App.Native;

namespace FocusManager.App.Views.Dialogs;

public partial class NewProjectWindow : Window
{
    public NewProjectWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        Loaded += (_, _) => NameBox.Focus();
    }

    public string ProjectName => NameBox.Text.Trim();

    public string? ProjectDescription
        => string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("项目名称不能为空。");

            NameBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
