using System.Windows;
using System.Windows.Controls;
using FocusManager.App.Services;
using FocusManager.App.Views.Dialogs;
using Microsoft.Web.WebView2.Wpf;

namespace FocusManager.App.Views;

/// <summary>
/// 内置 ChatGPT 页面。
///
/// 这个页面刻意做得很薄：它只负责「要不要现在创建 WebView2」，
/// 真正的环境管理在 <see cref="WebViewService"/> 里。
/// 页面本身在程序启动时就会创建（和其他页面一样常驻），
/// 但 WebView2 控件只有用户点按钮才会出现。
/// </summary>
public partial class ChatGptView : UserControl
{
    private readonly WebViewService? _webViewService;
    private WebView2? _webView;

    public ChatGptView()
    {
        InitializeComponent();

        if (AppServices.Current is { } services)
        {
            _webViewService = new WebViewService(services.DataDirectory);
        }
        else
        {
            OpenChatButton.IsEnabled = false;
            StatusText.Text = "数据目录没有初始化，暂时打不开网页。";
        }
    }

    /// <summary>网页此刻是否真的存在（用来判断离开页面时要不要释放）。</summary>
    public bool IsWebViewOpen => _webView is not null;

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        await OpenWebViewAsync();
    }

    private async Task OpenWebViewAsync()
    {
        if (_webViewService is null || _webView is not null)
        {
            return;
        }

        if (!WebViewService.IsRuntimeAvailable(out var version))
        {
            AppMessage.Show(
                Window.GetWindow(this),
                "这台电脑上找不到 WebView2 运行时，网页打不开。\n\n"
                + "请到微软官方页面下载「Evergreen Bootstrapper」安装一次，然后重新打开本程序：\n"
                + "https://developer.microsoft.com/microsoft-edge/webview2/",
                "缺少 WebView2 运行时",
                MessageKind.Warning);
            return;
        }

        OpenChatButton.IsEnabled = false;
        BusyPanel.Visibility = Visibility.Visible;
        StatusText.Text = "正在打开网页…";

        try
        {
            // 顺序很重要：先把控件放进界面，再初始化内核。
            // 不在界面里的 WebView2 调 EnsureCoreWebView2Async 会一直等下去。
            var view = WebViewService.CreateControl();
            HostPanel.Children.Add(view);
            _webView = view;

            await _webViewService.InitializeAsync(view);

            PlaceholderPanel.Visibility = Visibility.Collapsed;
            BusyPanel.Visibility = Visibility.Collapsed;
            ReloadButton.IsEnabled = true;
            CloseWebViewButton.IsEnabled = true;
            StatusText.Text = version is null
                ? "网页已打开"
                : $"网页已打开（WebView2 运行时 {version}）";
        }
        catch (Exception ex)
        {
            BusyPanel.Visibility = Visibility.Collapsed;
            OpenChatButton.IsEnabled = true;
            StatusText.Text = "网页没有打开";
            AppMessage.Show(
                Window.GetWindow(this),
                "打开网页时出错了：\n\n" + ex.Message,
                "网页没打开",
                MessageKind.Warning);
        }
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (_webView?.CoreWebView2 is { } core)
        {
            core.Reload();
        }
    }

    private void OnCloseWebViewClick(object sender, RoutedEventArgs e)
    {
        ReleaseWebView();
    }

    /// <summary>
    /// 关掉网页、把控件释放掉。用户数据目录保持不动，所以登录状态还在。
    /// 主窗口切到别的页面时也会调用这里。
    /// </summary>
    public void ReleaseWebView()
    {
        if (_webView is null)
        {
            return;
        }

        HostPanel.Children.Remove(_webView);
        _webView.Dispose();
        _webView = null;

        PlaceholderPanel.Visibility = Visibility.Visible;
        BusyPanel.Visibility = Visibility.Collapsed;
        OpenChatButton.IsEnabled = true;
        ReloadButton.IsEnabled = false;
        CloseWebViewButton.IsEnabled = false;
        StatusText.Text = "网页已经关掉了，登录状态还留着";
    }
}
