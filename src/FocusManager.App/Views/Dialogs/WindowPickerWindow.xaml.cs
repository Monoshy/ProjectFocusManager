using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FocusManager.App.Native;
using FocusManager.App.Services;

namespace FocusManager.App.Views.Dialogs;

/// <summary>
/// 列出当前开着窗口的程序，让用户挑一个加进应用列表。
/// 这样比让用户自己去硬盘上找 .exe 容易得多。
/// </summary>
public partial class WindowPickerWindow : Window
{
    public WindowPickerWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);

        Reload();

        Loaded += (_, _) =>
        {
            if (WindowListBox.Items.Count > 0)
            {
                WindowListBox.SelectedIndex = 0;
                WindowListBox.Focus();
            }
        };
    }

    public ObservableCollection<RunningWindow> Windows { get; } = [];

    public RunningWindow? SelectedWindow => WindowListBox.SelectedItem as RunningWindow;

    private void Reload()
    {
        Windows.Clear();

        foreach (var window in RunningWindowList.GetVisibleWindows())
        {
            Windows.Add(window);
        }

        WindowListBox.ItemsSource = Windows;

        if (Windows.Count == 0)
        {
            EmptyHintText.Text = "现在没有找到可以添加的程序。先把那个学习软件打开，再回来点一次。";
            EmptyHintBorder.Visibility = Visibility.Visible;
            ChooseButton.IsEnabled = false;
        }
    }

    private void OnChooseClick(object sender, RoutedEventArgs e)
    {
        if (SelectedWindow is null)
        {
            EmptyHintText.Text = "请先在列表里选一个程序。";
            EmptyHintBorder.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedWindow is not null)
        {
            DialogResult = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
