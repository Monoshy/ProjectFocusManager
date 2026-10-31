using FocusManager.Core.Data;

namespace FocusManager.Tests.TestSupport;

/// <summary>
/// 每个测试用一个全新的临时数据库，互不影响。
///
/// 位置放在测试程序自己的输出目录下（bin\...\testdata\随机编号\focus.db），
/// 这样不会写到系统临时目录（这台机器上 %TEMP% 不可写），
/// 也不会污染你的正式数据；删掉 bin 目录即可全部清理。
/// </summary>
internal sealed class TempDatabase : IDisposable
{
    private bool _disposed;

    public TempDatabase()
    {
        Root = Path.Combine(AppContext.BaseDirectory, "testdata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        DatabasePath = Path.Combine(Root, "focus.db");
        BackupDirectory = Path.Combine(Root, "backups");

        Service = new DatabaseService(DatabasePath, BackupDirectory);
        Service.Initialize();
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string BackupDirectory { get; }

    public DatabaseService Service { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 连接池会一直抓着文件句柄，测试结束前放掉，避免后续读取被占用。
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
