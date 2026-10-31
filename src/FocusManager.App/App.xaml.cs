using System.IO;
using System.Windows;
using System.Windows.Threading;
using FocusManager.App.Services;
using FocusManager.App.Views.Dialogs;

namespace FocusManager.App;

public partial class App : Application
{
    /// <summary>
    /// 单实例锁。必须一直留着，直到程序退出为止——丢了它，第二个进程就能启动了。
    /// </summary>
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Velopack 安装器钩子（首次安装、更新后重启等）必须最先执行，
        // 否则 vpk 打包校验不过，安装/升级流程也收不到这些参数。
        Velopack.VelopackApp.Build().Run();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        // 第一步就要判断「是不是已经有一个在跑了」，绝不能先建数据库、先开窗口。
        // 否则第二次双击 EXE 会多出一个进程、多一个数据库写入者。
        _singleInstance = SingleInstance.Acquire();

        if (!_singleInstance.IsOwner)
        {
            // 已经有实例在跑了。叫它把窗口显示出来，然后本进程立刻退出——
            // 这里不能弹对话框等用户点「确定」，否则任务管理器里会短暂出现两个进程，
            // 而「同时只有一个」正是这次要修的东西。
            var woke = SingleInstance.TryNotifyExisting();

            if (!woke)
            {
                // 连不上说明那个实例卡住了。这种时候才值得打扰用户，告诉他去托盘里找。
                MessageBox.Show(
                    "Project Focus Manager 已经在运行。\n\n"
                    + "没能把它的窗口叫到前面，请从任务栏右下角的系统托盘图标打开"
                    + "（Windows 11 里可能在「隐藏的图标 ^」中）。",
                    "Project Focus Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            _singleInstance.Dispose();
            _singleInstance = null;

            Shutdown(0);

            return;
        }

        var dataDirectory = AppPaths.ResolveDataDirectory(out var tried);

        if (dataDirectory is null)
        {
            MessageBox.Show(
                "程序找不到可以保存数据的位置，无法启动。\n\n" +
                "已经尝试过这些文件夹，但都写不进去：\n" +
                string.Join("\n", tried) +
                "\n\n请检查这些文件夹的权限，或者把程序放到你有写权限的目录里再试。",
                "Project Focus Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        AppServices services;

        try
        {
            services = AppServices.Initialize(dataDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "数据库初始化失败，程序无法启动。\n\n" +
                "数据目录：" + dataDirectory + "\n\n" +
                "错误信息：" + ex.Message,
                "Project Focus Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        ThemeManager.ApplyFromSettings(services.Settings);

        StartupLog(services, "主题已应用，准备创建主窗口");

        var window = new MainWindow();

        StartupLog(services, "主窗口已创建，准备显示");

        MainWindow = window;
        window.Show();

        StartupLog(services, "主窗口已显示");

        // 从现在开始，第二个实例的「叫醒」请求会走到这里：把窗口显示出来并拉到前台。
        _singleInstance?.StartListening(Dispatcher, () =>
        {
            window.ShowMainWindow();

            AppMessage.Show(
                window,
                "Project Focus Manager 已经在运行，已为你打开现有窗口。",
                "已经开着了",
                MessageKind.Info);
        });

        CheckUnfinishedSessions(services, window);
    }

    /// <summary>
    /// 启动时看一眼有没有上次没正常结束的专注（断电、崩溃、被强制结束都会留下）。
    /// 有的话就问用户一句：按已经保存的内容结束，还是从现在接着计时。
    ///
    /// 这里绝不会把程序关闭期间的时间算进专注时间——计时靠的是运行时新建的
    /// 单调时钟，重启之后是重新开始的。
    /// </summary>
    private static void CheckUnfinishedSessions(AppServices services, MainWindow window)
    {
        try
        {
            var unfinished = services.Sessions.GetUnfinishedSessions();
            if (unfinished.Count == 0) return;

            var session = unfinished[0];
            var project = services.Projects.FindById(session.ProjectId);

            var dialog = new RecoveryWindow(
                project?.Name ?? "（这个项目已经被删掉了）",
                session,
                unfinished.Count - 1)
            {
                Owner = window
            };

            dialog.ShowDialog();

            if (dialog.Choice == RecoveryChoice.Resume)
                services.Sessions.ResumeUnfinishedSession(session.Id);
            else
                services.Sessions.FinishUnfinishedSession(session.Id);

            // 更早的那些也一起结束，不能让它们一直挂在库里。
            for (var i = 1; i < unfinished.Count; i++)
                services.Sessions.FinishUnfinishedSession(unfinished[i].Id);

            window.RefreshAfterRecovery();
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }

    /// <summary>
    /// 记一条启动日志。正常情况下用不到，但万一以后「双击没反应」，
    /// 有这几行就能知道卡在哪一步。
    /// </summary>
    /// <summary>写一行启动日志。出问题时这些日志是唯一能看的东西。</summary>
    public static void LogStartup(string message)
    {
        if (AppServices.Current is { } services)
            StartupLog(services, message);
    }

    private static void StartupLog(AppServices services, string message)
    {
        try
        {
            var logDirectory = Path.Combine(services.DataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            File.AppendAllText(
                Path.Combine(logDirectory, "startup.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 日志写不进去不能影响程序启动。
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteErrorLog(e.Exception);

        MessageBox.Show(
            "程序遇到了一个没有预料到的问题，但已经帮你拦下来了，不会直接闪退。\n\n" +
            "错误信息：" + e.Exception.Message + "\n\n" +
            "详细信息已经记录在数据目录的 logs\\error.log 里。",
            "Project Focus Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteErrorLog(exception);
        }
    }

    /// <summary>
    /// 程序退出时把单实例锁交还出去，否则下一个进程会以为还有人在跑。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }

    private static void WriteErrorLog(Exception exception)
    {
        try
        {
            var services = AppServices.Current;
            if (services is null) return;

            var logDirectory = Path.Combine(services.DataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            var path = Path.Combine(logDirectory, "error.log");

            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 记日志本身失败就算了，不能因为记日志再崩一次。
        }
    }
}
