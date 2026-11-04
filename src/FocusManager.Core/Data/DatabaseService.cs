using Microsoft.Data.Sqlite;

namespace FocusManager.Core.Data;

/// <summary>
/// 数据库连接与结构升级的统一入口。
///
/// 职责只有三件：
/// 1. 打开连接（并设置好 SQLite 的安全与性能参数）；
/// 2. 启动时检查结构版本，需要时自动升级（升级前先备份）；
/// 3. 提供手动备份能力。
///
/// 它不关心「项目」「会话」这些业务概念，那些在 Service 层。
/// </summary>
public sealed class DatabaseService
{
    /// <summary>当前程序支持的数据库结构版本。以后加表就 +1 并写新的迁移方法。</summary>
    public const int TargetSchemaVersion = 2;

    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public DatabaseService(string databasePath, string? backupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));

        _databasePath = Path.GetFullPath(databasePath);

        var directory = Path.GetDirectoryName(_databasePath);
        _backupDirectory = backupDirectory
            ?? Path.Combine(string.IsNullOrEmpty(directory) ? "." : directory, "backups");
    }

    public string DatabasePath => _databasePath;

    public string BackupDirectory => _backupDirectory;

    /// <summary>
    /// 程序启动时调用：确保目录存在、数据库可打开、结构是最新的。
    /// 重复调用是安全的（不会重复建表、不会重复备份）。
    /// </summary>
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();

        var currentVersion = ReadSchemaVersion(connection);

        if (currentVersion > TargetSchemaVersion)
        {
            throw new InvalidOperationException(
                $"数据库结构版本是 {currentVersion}，比当前程序支持的 {TargetSchemaVersion} 更新。" +
                "请先升级程序，不要用旧版本打开新数据库，否则可能损坏数据。");
        }

        if (currentVersion == TargetSchemaVersion)
        {
            return;
        }

        // 升级前先备份，出问题可以退回。
        if (currentVersion > 0 && File.Exists(_databasePath))
        {
            Backup($"升级前-v{currentVersion}");
        }

        using var transaction = connection.BeginTransaction();

        if (currentVersion < 1)
        {
            Migrations.ApplyV1(connection, transaction);
        }

        if (currentVersion < 2)
        {
            Migrations.ApplyV2(connection, transaction);
        }

        WriteSchemaVersion(connection, TargetSchemaVersion, transaction);
        transaction.Commit();
    }

    /// <summary>读取当前数据库结构版本。</summary>
    public int GetSchemaVersion()
    {
        using var connection = OpenConnection();
        return ReadSchemaVersion(connection);
    }

    /// <summary>
    /// 打开一个连接。调用方负责 using 释放。
    /// </summary>
    public SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        // WAL 模式：读写互不阻塞，程序崩溃后数据更安全；
        // synchronous=NORMAL 在 WAL 下是推荐值，兼顾安全和速度；
        // busy_timeout：万一有并发访问，最多等 5 秒而不是直接报错。
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// 备份数据库，返回备份文件路径。
    /// 使用 SQLite 官方的 VACUUM INTO，能在程序运行中做出一致性快照
    /// （比直接复制文件更安全，尤其是 WAL 模式下）。
    /// </summary>
    public string Backup(string label)
    {
        Directory.CreateDirectory(_backupDirectory);

        var safeLabel = string.Join("_", label.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var baseName = Path.GetFileNameWithoutExtension(_databasePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var target = Path.Combine(_backupDirectory, $"{baseName}-{stamp}-{safeLabel}.db");
        var counter = 1;
        while (File.Exists(target))
        {
            target = Path.Combine(_backupDirectory, $"{baseName}-{stamp}-{safeLabel}-{counter}.db");
            counter++;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $target";
        command.Parameters.AddWithValue("$target", target);
        command.ExecuteNonQuery();

        return target;
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static void WriteSchemaVersion(SqliteConnection connection, int version, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // PRAGMA 不支持参数绑定，这里的 version 是代码里的常量，不存在注入风险。
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }
}
