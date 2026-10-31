using System.Windows;
using System.Windows.Media;
using FocusManager.App.Native;
using FocusManager.App.Services;
using FocusManager.App.Views;
using FocusManager.App.Views.Dialogs;
using FocusManager.Core.Services;

namespace FocusManager.App;

/// <summary>
/// 主窗口只做两件事：在几个页面之间切换，以及显示当前主题。
/// 具体的界面和逻辑都在各自的 View 里，不堆在这里。
/// </summary>
public partial class MainWindow : Window
{
    private readonly FocusView _focusView = new();
    private readonly ProjectsView _projectsView = new();
    private readonly ApplicationsView _applicationsView = new();
    private readonly IdeasView _ideasView = new();
    private readonly ChatGptView _chatView = new();
    private readonly SettingsView _settingsView = new();

    private TrayService? _tray;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => DwmHelper.ApplyTitleBarTheme(this, ThemeManager.IsDark);
        ThemeManager.ThemeChanged += (_, _) => UpdateThemeButton();
        UpdateThemeButton();

        ShowPage(_focusView);

        CreateTray();

        // 关窗口默认只是藏到托盘，要真的退出走托盘菜单里的「退出程序」。
        Closing += OnWindowClosing;
        StateChanged += OnWindowStateChanged;
    }

    /// <summary>系统托盘：图标、悬停提示、右键菜单。</summary>
    private void CreateTray()
    {
        try
        {
            _tray = CreateTrayCore();
            App.LogStartup("系统托盘已就绪");
        }
        catch (Exception ex)
        {
            // 托盘起不来也不能让主程序打不开。
            _tray = null;
            App.LogStartup("系统托盘创建失败：" + ex.GetType().Name + " - " + ex.Message);
        }

        if (_tray is not null)
            _focusView.FocusStateChanged += (_, _) => _tray?.Refresh();
    }

    private TrayService CreateTrayCore()
    {
        var tray = new TrayService(
            () => _focusView.CreateTraySnapshot(),
            ShowMainWindow,
            MinimizeToTray,
            () => _focusView.TrayTogglePause(),
            () => _focusView.TrayFinish(),
            ExitApplication);

        return tray;
    }

    /// <summary>
    /// 把主窗口显示出来并拉到前台。托盘菜单、第二个实例的唤醒请求都走这里。
    /// </summary>
    public void ShowMainWindow()
    {
        Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        ForegroundHelper.ForceActivate(this);
        _tray?.Refresh();
    }

    /// <summary>把窗口收进系统托盘，计时继续。</summary>
    private void MinimizeToTray()
    {
        Hide();
        _tray?.Refresh();
    }

    /// <summary>
    /// 点右上角关闭按钮。
    ///
    /// 三种情况分开处理（需求里特别强调过不要把关闭和最小化搞混）：
    /// 1. 设置里选了「每次询问」——弹自己的窗口问一句，还可以顺手记住选择；
    /// 2. 选了「最小化到系统托盘」——直接收进托盘，计时继续；
    /// 3. 选了「直接退出程序」——先存一次检查点，再退出。
    /// </summary>
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting)
            return;

        // 先一律取消：真正退出由 ApplyCloseBehavior 调 ExitApplication 完成，
        // 这样「询问」过程中窗口不会先关掉一半。
        e.Cancel = true;

        var services = AppServices.Current;
        var behavior = CloseBehaviorRule.Resolve(services?.Settings.Get(CloseBehaviorRule.SettingKey));

        if (behavior == CloseBehavior.Ask)
        {
            var dialog = new CloseChoiceWindow(CloseBehavior.MinimizeToTray) { Owner = this };

            if (dialog.ShowDialog() != true)
                return;

            behavior = dialog.Choice;

            if (dialog.Remember)
                services?.Settings.Set(CloseBehaviorRule.SettingKey, CloseBehaviorRule.ToSettingValue(behavior));
        }

        ApplyCloseBehavior(behavior);
    }

    private void ApplyCloseBehavior(CloseBehavior behavior)
    {
        if (behavior == CloseBehavior.Exit)
        {
            _focusView.SaveCheckpointBeforeExit();
            ExitApplication();
            return;
        }

        MinimizeToTray();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // 最小化就是正常最小化到任务栏：任务栏图标还在，点一下就能回来。
        // 想收进托盘请用托盘菜单里的「最小化到托盘」，或者设置里的关闭行为。
        if (WindowState == WindowState.Minimized)
            _tray?.Refresh();
    }

    /// <summary>真正退出程序（托盘菜单里点「退出程序」）。</summary>
    public void ExitApplication()
    {
        _exiting = true;

        _tray?.Dispose();
        _tray = null;

        Application.Current.Shutdown();
    }

    private void OnNavFocusChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null) return;

        _focusView.RefreshFromDatabase();
        ShowPage(_focusView);
    }

    private void OnNavProjectsChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null) return;

        _projectsView.RefreshFromDatabase();
        ShowPage(_projectsView);
    }

    private void OnNavApplicationsChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null) return;

        _applicationsView.RefreshFromDatabase();
        ShowPage(_applicationsView);
    }

    private void OnNavIdeasChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null) return;

        _ideasView.RefreshFromDatabase();
        ShowPage(_ideasView);
    }

    private void OnNavChatChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null)
        {
            return;
        }

        ShowPage(_chatView);
    }

    private void OnNavSettingsChecked(object sender, RoutedEventArgs e)
    {
        if (PageHost is null) return;

        ShowPage(_settingsView);
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
    {
        var services = AppServices.Current;

        if (services is null) return;

        ThemeManager.Toggle(services.Settings);
    }

    /// <summary>导航栏底部那个按钮的文字和图标，要跟着当前主题变。</summary>
    private void UpdateThemeButton()
    {
        if (ThemeText is null || ThemeIcon is null) return;

        ThemeText.Text = ThemeManager.DisplayName;
        ThemeIcon.Data = (Geometry)FindResource(ThemeManager.IsDark ? "IconMoon" : "IconSun");
    }

    /// <summary>
    /// 切换页面。离开「内置 ChatGPT」时把网页关掉，把内存还回去
    /// （登录状态在用户数据目录里，不受影响）。
    /// </summary>
    private void ShowPage(object page)
    {
        if (!ReferenceEquals(PageHost.Content, page) && PageHost.Content is ChatGptView leaving)
        {
            leaving.ReleaseWebView();
        }

        PageHost.Content = page;
    }

    /// <summary>
    /// 启动时处理完「上次没结束的那一轮」之后，把两个页面的数据都重新读一遍，
    /// 免得还显示着旧状态。
    /// </summary>
    public void RefreshAfterRecovery()
    {
        _focusView.RefreshAfterRecovery();
        _projectsView.RefreshFromDatabase();
        _applicationsView.RefreshFromDatabase();
        _ideasView.RefreshFromDatabase();
    }
}
