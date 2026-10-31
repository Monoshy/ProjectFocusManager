using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;

namespace FocusManager.App.Services;

/// <summary>托盘图标的三种样子。</summary>
public enum TrayState
{
    Idle,
    Running,
    Paused,
}

/// <summary>托盘每次刷新需要的全部信息。</summary>
public sealed record TraySnapshot(TrayState State, string ToolTipText);

/// <summary>
/// 系统托盘图标：显示状态、显示当前项目与累计时间、提供基本计时操作。
///
/// 设计上的取舍：
/// - 图标只做三态（空闲 / 专注中 / 暂停中），一眼能看出来；
/// - 悬停才显示准确时间，不在桌面上常驻任何悬浮窗；
/// - 专注中每秒钟刷新一次提示文字，空闲时把计时器停掉，不做无意义刷新。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _pauseItem;
    private readonly MenuItem _finishItem;
    private readonly DispatcherTimer _ticker;

    private readonly Func<TraySnapshot> _snapshotProvider;
    private readonly Action _showWindow;
    private readonly Action _minimizeToTray;
    private readonly Action _togglePause;
    private readonly Action _finishSession;
    private readonly Action _exit;

    private TrayState _state = TrayState.Idle;
    private string _toolTip = string.Empty;
    private bool _disposed;
    private bool _available;

    public TrayService(
        Func<TraySnapshot> snapshotProvider,
        Action showWindow,
        Action minimizeToTray,
        Action togglePause,
        Action finishSession,
        Action exit)
    {
        _snapshotProvider = snapshotProvider;
        _showWindow = showWindow;
        _minimizeToTray = minimizeToTray;
        _togglePause = togglePause;
        _finishSession = finishSession;
        _exit = exit;

        var showItem = new MenuItem { Header = "显示主界面" };
        showItem.Click += (_, _) => _showWindow();

        var minimizeItem = new MenuItem { Header = "最小化到托盘" };
        minimizeItem.Click += (_, _) => _minimizeToTray();

        _pauseItem = new MenuItem { Header = "暂停专注" };
        _pauseItem.Click += (_, _) => _togglePause();

        _finishItem = new MenuItem { Header = "结束本次专注" };
        _finishItem.Click += (_, _) => _finishSession();

        var exitItem = new MenuItem { Header = "退出程序" };
        exitItem.Click += (_, _) => _exit();

        var menu = new ContextMenu
        {
            Items =
            {
                showItem,
                minimizeItem,
                new Separator(),
                _pauseItem,
                _finishItem,
                new Separator(),
                exitItem,
            },
        };

        _icon = new TaskbarIcon
        {
            ToolTipText = "Project Focus Manager",
            ContextMenu = menu,
        };

        _icon.TrayLeftMouseUp += (_, _) => _showWindow();
        _icon.TrayMouseDoubleClick += (_, _) => _showWindow();

        // 顺序很重要：Shell_NotifyIcon 需要一个有效的图标句柄，
        // 先把空闲态图标装好，再创建托盘图标，否则 Create 会失败。
        ApplyIcon(TrayState.Idle);

        try
        {
            _icon.ForceCreate();
            _available = true;
        }
        catch (Exception)
        {
            // 托盘创建失败（极少见，比如资源管理器没起来）不应该影响主程序使用。
            _available = false;
        }

        _ticker = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _ticker.Tick += (_, _) => Refresh();

        Refresh();
    }

    /// <summary>状态变了（开始、暂停、继续、结束）之后调一下，立刻更新图标与菜单。</summary>
    public void Refresh()
    {
        if (_disposed || !_available)
            return;

        TraySnapshot snapshot;

        try
        {
            snapshot = _snapshotProvider();
        }
        catch (Exception)
        {
            return;
        }

        ApplyIcon(snapshot.State);

        if (!string.Equals(snapshot.ToolTipText, _toolTip, StringComparison.Ordinal))
        {
            _toolTip = snapshot.ToolTipText;
            _icon.ToolTipText = snapshot.ToolTipText;
        }

        _pauseItem.Header = snapshot.State == TrayState.Paused ? "继续专注" : "暂停专注";
        _pauseItem.IsEnabled = snapshot.State != TrayState.Idle;
        _finishItem.IsEnabled = snapshot.State != TrayState.Idle;

        var shouldTick = snapshot.State != TrayState.Idle;

        if (shouldTick && !_ticker.IsEnabled)
            _ticker.Start();
        else if (!shouldTick && _ticker.IsEnabled)
            _ticker.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _ticker.Stop();
        _icon.Dispose();
    }

    private void ApplyIcon(TrayState state)
    {
        if (state == _state && _icon.Icon is not null)
            return;

        _state = state;

        var name = state switch
        {
            TrayState.Running => "tray-running.ico",
            TrayState.Paused => "tray-paused.ico",
            _ => "tray-idle.ico",
        };

        try
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Resources/{name}"));

            if (resource is null)
                return;

            using var stream = resource.Stream;

            // 指定尺寸取 32×32 那一档：图标文件里的 256×256 是 PNG 压缩的，
            // 直接 new Icon(stream) 在部分机器上会取到它并出错。
            _icon.Icon = new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        }
        catch (Exception)
        {
            // 图标加载失败不影响功能，托盘会用系统默认图标。
        }
    }
}
