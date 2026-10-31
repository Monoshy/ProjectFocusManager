using System.Windows.Threading;

namespace FocusManager.App.Services;

/// <summary>
/// 保证整台电脑上只有一个 Project Focus Manager 在跑。
///
/// 两件东西配合：
/// 1. 一个具名的系统互斥量（Mutex）——第二个进程一启动就发现名字被占了，
///    于是它不建窗口、不打开数据库，直接退出；
/// 2. 一个具名的系统事件（EventWaitHandle）——第二个进程用它给第一个进程
///    发一句「醒醒」，第一个进程收到之后把窗口显示出来并拉到前台。
///
/// 为什么用事件而不是命名管道：管道要握手、要读回复，一旦某一端卡住就会互相等待；
/// 事件只是"按一下铃"，发完就走，没有可以卡住的环节。
///
/// 为什么不按进程名判断：同名程序可能存在，杀进程或改名都会让判断失灵。
/// 为什么不怕上一个实例崩溃：互斥量在进程结束时由系统自动释放，
/// 这里再用 AbandonedMutexException 兜一次，崩溃之后照样能正常启动。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ProjectFocusManager.SingleInstance";
    private const string WakeEventName = @"Local\ProjectFocusManager.Wake";

    private readonly Mutex _mutex;

    private EventWaitHandle? _wakeEvent;
    private Thread? _listener;
    private volatile bool _disposed;

    private SingleInstance(Mutex mutex, bool isOwner, EventWaitHandle? wakeEvent)
    {
        _mutex = mutex;
        IsOwner = isOwner;
        _wakeEvent = wakeEvent;
    }

    /// <summary>本进程是不是那个「唯一实例」。</summary>
    public bool IsOwner { get; }

    /// <summary>
    /// 尝试成为唯一实例。返回的对象一定要留到程序退出（Dispose 之前别丢），
    /// 否则互斥量被回收，单实例就失效了。
    /// </summary>
    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);

        bool owned;

        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例是崩溃或被强杀结束的，系统已经把互斥量交给我们了。
            owned = true;
        }
        catch (Exception)
        {
            // 极少数情况下拿不到互斥量（权限、名字冲突）：
            // 这时候宁可让程序能启动，也不要卡死在启动阶段。
            owned = true;
        }

        if (!owned)
            return new SingleInstance(mutex, false, null);

        // 成为唯一实例之后马上把"门铃"建好，这样哪怕用户一秒钟内双击第二次，
        // 第二个进程也能找到它。
        EventWaitHandle? wakeEvent = null;

        try
        {
            wakeEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, WakeEventName);
        }
        catch (Exception)
        {
            // 建不出来就退化成"第二个实例弹个提示然后退出"，不影响主程序运行。
        }

        return new SingleInstance(mutex, true, wakeEvent);
    }

    /// <summary>
    /// 开始等别的进程按门铃。只在唯一实例里调用。
    /// 收到之后在 UI 线程上执行 <paramref name="onWake"/>。
    /// </summary>
    public void StartListening(Dispatcher dispatcher, Action onWake)
    {
        if (!IsOwner || _disposed || _listener is not null || _wakeEvent is null)
            return;

        _listener = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    // 带超时地等，这样 Dispose 之后线程能自己退出。
                    if (!_wakeEvent.WaitOne(500))
                        continue;
                }
                catch (Exception)
                {
                    return;
                }

                if (_disposed)
                    return;

                dispatcher.BeginInvoke(new Action(onWake), DispatcherPriority.Normal);
            }
        })
        {
            IsBackground = true,
            Name = "ProjectFocusManager.WakeListener",
        };

        _listener.Start();
    }

    /// <summary>
    /// 第二个实例调用：按一下已经在跑的那个实例的门铃。成功返回 true。
    /// </summary>
    public static bool TryNotifyExisting()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(WakeEventName, out var handle))
                return false;

            using (handle)
            {
                return handle.Set();
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _wakeEvent?.Set();
        }
        catch (Exception)
        {
            // 让监听线程醒过来好退出，失败就算了。
        }

        _wakeEvent?.Dispose();
        _wakeEvent = null;

        if (IsOwner)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception)
            {
                // 不是当前线程持有、或者已经释放过，忽略即可。
            }
        }

        _mutex.Dispose();
    }
}
