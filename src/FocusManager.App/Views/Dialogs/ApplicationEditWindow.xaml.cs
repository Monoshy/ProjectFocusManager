using System.IO;
using System.Windows;
using FocusManager.App.Native;
using FocusManager.Core.Services;

namespace FocusManager.App.Views.Dialogs;

/// <summary>
/// 添加或修改一个学习软件。
///
/// 「程序文件」允许留空：有些程序（比如预置的百度网盘、CCtalk）本来就没有登记路径，
/// 靠进程名识别窗口就够了。
/// </summary>
public partial class ApplicationEditWindow : Window
{
    private readonly bool _isEdit;
    private readonly string _fixedProcessName;

    public ApplicationEditWindow(
        string heading,
        string name = "",
        string processName = "",
        string executablePath = "",
        bool isEdit = false)
    {
        InitializeComponent();

        _isEdit = isEdit;
        _fixedProcessName = processName;

        HeadingText.Text = heading;
        Title = heading;
        NameBox.Text = name;
        PathBox.Text = executablePath;

        if (isEdit)
        {
            HintText.Text = "改完名字或者换了程序位置之后，点保存。";
        }

        UpdateProcessHint();

        PathBox.TextChanged += (_, _) => UpdateProcessHint();

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ApplicationName => NameBox.Text.Trim();

    public string ExecutablePath => PathBox.Text.Trim();

    /// <summary>进程名由路径推出来；没有路径时用传进来的那个（从运行中的窗口添加时用）。</summary>
    public string ProcessName => ExecutablePath.Length > 0
        ? ApplicationService.GuessProcessName(ExecutablePath)
        : _fixedProcessName;

    private void UpdateProcessHint()
    {
        var processName = ProcessName;

        ProcessHintText.Text = processName.Length > 0
            ? $"识别窗口时用的进程名：{processName}"
            : "提示：不填程序文件也可以，但这样就没法自动识别它的窗口。";
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择这个学习软件的程序文件",
            Filter = "程序文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        PathBox.Text = dialog.FileName;

        if (NameBox.Text.Trim().Length == 0)
        {
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (NameBox.Text.Trim().Length == 0)
        {
            ShowError("名称不能为空，起一个你认得出的名字。");
            NameBox.Focus();
            return;
        }

        if (PathBox.Text.Trim().Length > 0 && !File.Exists(PathBox.Text.Trim()))
        {
            ShowError("找不到这个程序文件，可能已经被移动或删除。可以点「浏览…」重新选。");
            return;
        }

        if (!_isEdit && ProcessName.Length == 0)
        {
            ShowError("没有拿到这个程序的进程名。可以点「浏览…」选它的 .exe 文件。");
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
