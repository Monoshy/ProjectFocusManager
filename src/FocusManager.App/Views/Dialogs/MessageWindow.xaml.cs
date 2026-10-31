using System.Windows;
using System.Windows.Media;
using FocusManager.App.Native;

namespace FocusManager.App.Views.Dialogs;

/// <summary>提示框的样式：普通信息、成功、警告、错误。</summary>
public enum MessageKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 程序自己的提示框，代替系统默认的 MessageBox，这样深色主题下也是同一套外观。
///
/// 两种用法：只说一句话（只有「知道了」），或者问一句要不要做（有「取消」和确认按钮）。
/// </summary>
public partial class MessageWindow : Window
{
    /// <summary>只提示，不需要用户做选择。</summary>
    public MessageWindow(string title, string message, MessageKind kind)
        : this(title, message, kind, okText: "知道了", cancelText: null)
    {
    }

    /// <summary>需要用户确认：cancelText 不为空时，会多出一个「取消」按钮。</summary>
    public MessageWindow(string title, string message, MessageKind kind, string okText, string? cancelText)
    {
        InitializeComponent();

        ApplyContent(title, message, kind);
        OkButton.Content = okText;

        if (cancelText is not null)
        {
            CancelButton.Content = cancelText;
            CancelButton.Visibility = Visibility.Visible;

            // 需要确认时，默认焦点给「取消」，避免随手回车就把数据删了。
            Loaded += (_, _) => CancelButton.Focus();
        }
        else
        {
            OkButton.IsCancel = true;
            Loaded += (_, _) => OkButton.Focus();
        }

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
    }

    /// <summary>
    /// 换掉窗口里的文字。同一个提示框还开着的时候，后来的提示直接改内容，
    /// 而不是再叠一个新窗口 —— 连续点几次程序时，不该堆出一摞「已经开着了」。
    /// </summary>
    public void UpdateContent(string title, string message, MessageKind kind)
    {
        ApplyContent(title, message, kind);
    }

    private void ApplyContent(string title, string message, MessageKind kind)
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        AccentBar.Background = BrushFor(kind);
    }

    /// <summary>用户是否点了确认按钮。</summary>
    public bool Confirmed { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private Brush BrushFor(MessageKind kind)
    {
        var key = kind switch
        {
            MessageKind.Success => "FocusBrush",
            MessageKind.Warning => "PauseBrush",
            MessageKind.Error => "DangerBrush",
            _ => "AccentBrush",
        };

        return (Brush)FindResource(key);
    }
}

/// <summary>弹提示框的简便入口。</summary>
public static class AppMessage
{
    /// <summary>当前还开着的提示框。用来避免同一时刻叠出多个。</summary>
    private static MessageWindow? _open;

    /// <summary>
    /// 弹一个提示框；owner 为空时以主窗口为父窗口。
    ///
    /// 这里用 BeginInvoke 把弹窗排到当前这一轮输入处理之后再显示：
    /// 直接在按钮的 Click 里 ShowDialog，弹窗有可能被同一次点击/回车的消息顺手确认掉，
    /// 用户会以为程序没反应。延后一个消息循环再弹就不会有这个问题。
    /// </summary>
    public static void Show(
        Window? owner,
        string message,
        string title = "提示",
        MessageKind kind = MessageKind.Warning)
    {
        var app = Application.Current;

        if (app is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ownerWindow = owner ?? app.MainWindow;

        app.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                // 已经有一个提示框开着，就把它的内容换掉并叫到前面，不再叠一个新的。
                if (_open is { IsLoaded: true } open)
                {
                    open.UpdateContent(title, message, kind);
                    if (open.WindowState == WindowState.Minimized)
                    {
                        open.WindowState = WindowState.Normal;
                    }

                    open.Activate();
                    return;
                }

                var window = new MessageWindow(title, message, kind)
                {
                    Owner = ownerWindow,
                };

                _open = window;

                try
                {
                    window.ShowDialog();
                }
                finally
                {
                    if (ReferenceEquals(_open, window))
                    {
                        _open = null;
                    }
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 问一句「要不要这么做」。用户点确认返回 true，点取消或直接关窗返回 false。
    ///
    /// 这个方法是同步的：调用方要拿到结果才能决定删不删，所以不能用 BeginInvoke 延后。
    /// </summary>
    public static bool Confirm(
        Window? owner,
        string message,
        string title,
        string okText,
        string cancelText = "取消",
        MessageKind kind = MessageKind.Warning)
    {
        var app = Application.Current;

        if (app is null)
            return MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

        var window = new MessageWindow(title, message, kind, okText, cancelText)
        {
            Owner = owner ?? app.MainWindow,
        };

        window.ShowDialog();
        return window.Confirmed;
    }
}
