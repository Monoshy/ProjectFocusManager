using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FocusManager.App.Services;

/// <summary>
/// 内置 ChatGPT 网页的唯一入口。
///
/// 需求第五节的四条硬约束，在这里落实成四件事：
/// 1. 程序启动时<strong>不</strong>初始化 WebView2 —— 本类只在用户点「打开 ChatGPT」时被调用；
/// 2. 整个程序只有一个 <see cref="CoreWebView2Environment"/>，因此浏览器进程只有一份；
/// 3. 用户数据目录独立（<c>数据目录\WebView2</c>），登录状态能保留，不需要每次重新登录；
/// 4. 离开页面时由调用方释放控件，但数据目录一律不动。
///
/// 需要说清楚的一点：释放控件不等于浏览器进程立刻退出。
/// msedgewebview2.exe 什么时候回收内存由 WebView2 运行时自己决定，
/// 我们只能做到「不再持有控件、不再刷新页面」，真实占用见 docs/内存测试结果.md。
/// </summary>
public sealed class WebViewService : IDisposable
{
    /// <summary>官方地址，不经过任何中转。</summary>
    public const string StartUrl = "https://chatgpt.com/";

    private readonly string _userDataFolder;
    private CoreWebView2Environment? _environment;
    private bool _disposed;

    public WebViewService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _userDataFolder = Path.Combine(dataDirectory, "WebView2");
    }

    /// <summary>网页的用户数据目录（登录状态就存在这里）。</summary>
    public string UserDataFolder => _userDataFolder;

    /// <summary>
    /// 本机是否装了 WebView2 运行时。没装时返回 false，并把版本号设为 null。
    /// 用运行时自己的接口问，比查注册表可靠。
    /// </summary>
    public static bool IsRuntimeAvailable(out string? version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException)
        {
            version = null;
            return false;
        }
    }

    /// <summary>
    /// 取得（第一次会创建）唯一的那份浏览器环境。
    /// 环境本身很轻，浏览器进程是跟着控件走的。
    /// </summary>
    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_environment is not null)
        {
            return _environment;
        }

        Directory.CreateDirectory(_userDataFolder);

        _environment = await CoreWebView2Environment
            .CreateAsync(browserExecutableFolder: null, userDataFolder: _userDataFolder, options: null)
            .ConfigureAwait(true);

        return _environment;
    }

    /// <summary>
    /// 建一个还没初始化的控件。调用方要<strong>先把它放进界面</strong>，
    /// 再调用 <see cref="InitializeAsync"/> —— 不在界面里的控件初始化会一直等下去。
    /// </summary>
    public static WebView2 CreateControl() => new()
    {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
    };

    /// <summary>控件已经在界面里之后，初始化内核并打开 ChatGPT。</summary>
    public async Task InitializeAsync(WebView2 view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var environment = await GetEnvironmentAsync().ConfigureAwait(true);

        await view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        if (view.CoreWebView2 is not { } core)
        {
            throw new InvalidOperationException("网页内核没有初始化成功。");
        }

        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultContextMenusEnabled = true;
        settings.IsSwipeNavigationEnabled = false;

        core.Navigate(StartUrl);
    }

    /// <summary>释放环境句柄。控件本身由界面负责释放。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _environment = null;
    }

}
