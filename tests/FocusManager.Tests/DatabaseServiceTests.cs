using FocusManager.Core.Data;
using FocusManager.Tests.TestSupport;

namespace FocusManager.Tests;

/// <summary>
/// 数据库的测试：建表、版本号、备份、以及「用旧程序打开新数据库」的保护。
/// </summary>
public sealed class DatabaseServiceTests
{
    [Fact]
    public void Initialize_CreatesAllCoreTables()
    {
        using var db = new TempDatabase();
        using var connection = db.Service.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        var tables = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("Projects", tables);
        Assert.Contains("Applications", tables);
        Assert.Contains("FocusSessions", tables);
        Assert.Contains("PauseRecords", tables);
        Assert.Contains("FocusIntervals", tables);
        Assert.Contains("IdeaNotes", tables);
        Assert.Contains("AppSettings", tables);
        Assert.Contains("SessionCheckpoints", tables);
        Assert.Contains("ProjectApplications", tables);
        Assert.Contains("SessionApplications", tables);
    }

    [Fact]
    public void Initialize_UpgradesV1DatabaseToV2_KeepingData()
    {
        using var db = new TempDatabase();

        // 造一个「只有 v1」的库：把 v2 的两张表去掉，版本号退回 1，再塞一条老数据。
        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                DROP TABLE SessionApplications;
                DROP TABLE ProjectApplications;
                PRAGMA user_version = 1;
                INSERT INTO Projects (Name, CreatedAtUtc) VALUES ('升级前的项目', '2026-10-31T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        db.Service.Initialize();

        Assert.Equal(2, db.Service.GetSchemaVersion());

        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            // 老数据还在。
            command.CommandText = "SELECT Name FROM Projects;";
            Assert.Equal("升级前的项目", command.ExecuteScalar());

            // 新表建好了。
            command.CommandText = """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'table' AND name IN ('ProjectApplications', 'SessionApplications');
                """;
            Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
        }

        // 升级前自动备份了一份 v1 的库。
        Assert.True(Directory.Exists(db.BackupDirectory));
        Assert.NotEmpty(Directory.GetFiles(db.BackupDirectory, "*升级前-v1*"));
    }

    [Fact]
    public void Initialize_SetsSchemaVersion()
    {
        using var db = new TempDatabase();
        Assert.Equal(DatabaseService.TargetSchemaVersion, db.Service.GetSchemaVersion());
    }

    [Fact]
    public void Initialize_Twice_IsSafe()
    {
        using var db = new TempDatabase();

        // 第二次初始化不应该报错，也不应该重建表把数据清掉。
        db.Service.Initialize();

        Assert.Equal(DatabaseService.TargetSchemaVersion, db.Service.GetSchemaVersion());
    }

    [Fact]
    public void Initialize_RefusesNewerDatabase()
    {
        using var db = new TempDatabase();

        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var error = Assert.Throws<InvalidOperationException>(() => db.Service.Initialize());
        Assert.Contains("99", error.Message);
    }

    [Fact]
    public void Backup_CreatesUsableCopy()
    {
        using var db = new TempDatabase();

        var backupPath = db.Service.Backup("测试备份");

        Assert.True(File.Exists(backupPath));
        Assert.True(new FileInfo(backupPath).Length > 0);

        // 备份文件本身必须是一个能打开的数据库，并且表结构完整。
        var backup = new DatabaseService(backupPath, db.BackupDirectory);
        Assert.Equal(DatabaseService.TargetSchemaVersion, backup.GetSchemaVersion());

        using var connection = backup.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'FocusIntervals';";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public void Backup_DoesNotOverwriteExistingFile()
    {
        using var db = new TempDatabase();

        var first = db.Service.Backup("同名");
        var second = db.Service.Backup("同名");

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void Timestamps_RoundTripAsUtc()
    {
        var original = new DateTimeOffset(2026, 10, 31, 6, 36, 12, TimeSpan.Zero).AddTicks(1234567);

        var text = SqliteValue.ToDb(original);
        var parsed = SqliteValue.FromDb(text);

        Assert.EndsWith("Z", text);
        Assert.Equal(original, parsed);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }

    [Fact]
    public void Backup_RestoreRecoversData()
    {
        // 备份恢复的真实演练：写点数据 → 备份 → 把主库删掉 → 用备份顶上 → 数据还在。
        // 这正是用户误删/损坏后唯一的自救路径，值得端到端验证而不是只看文件存在。
        using var db = new TempDatabase();

        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Projects (Name, CreatedAtUtc) VALUES ('恢复演练项目', '2026-10-31T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        var backupPath = db.Service.Backup("恢复演练");

        // 模拟主库损坏：清池放句柄 → 删主库（含 WAL 伴生文件）→ 备份改名顶上。
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = db.DatabasePath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        File.Copy(backupPath, db.DatabasePath);

        var restored = new DatabaseService(db.DatabasePath, db.BackupDirectory);
        restored.Initialize();

        using (var connection = restored.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Name FROM Projects;";
            Assert.Equal("恢复演练项目", command.ExecuteScalar());
        }
    }

    [Fact]
    public void MigrationRehearsal_V2ToV3_FollowsDocumentedRules()
    {
        // v2→v3 迁移演练：v2（项目辅助程序）已经真实发布，这里按 PROJECT_STATUS.md 里写死的
        // 迁移规则再往后预演一次，保证规则本身可行，而不是等到真写 v3 时才发现坑：
        //   1. 升级前先备份，备份里是升级前的数据；
        //   2. 升级后老数据原样保留；
        //   3. 旧程序拒绝打开升级后的库。
        using var db = new TempDatabase();

        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Projects (Name, CreatedAtUtc) VALUES ('迁移演练项目', '2026-10-31T00:00:00Z');
                """;
            command.ExecuteNonQuery();
        }

        // 第 1 步：按 Initialize() 里升级前备份的同一方式做快照。
        var preUpgradeBackup = db.Service.Backup("升级前-v2");

        // 第 2 步：扮演未来的 v3 程序——加一列、版本号推到 3。
        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                ALTER TABLE Projects ADD COLUMN DrillNote TEXT NULL;
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }

        // 第 3 步：备份还是纯 v2——没有新列，但老数据齐全。
        using (var backupConnection = new DatabaseService(preUpgradeBackup).OpenConnection())
        using (var command = backupConnection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Projects') WHERE name = 'DrillNote';";
            Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));

            command.CommandText = "SELECT Name FROM Projects;";
            Assert.Equal("迁移演练项目", command.ExecuteScalar());
        }

        // 第 4 步：老程序（只认到 v2）必须拒绝打开 v3 库，而不是硬着头皮读写。
        var stale = new DatabaseService(db.DatabasePath, db.BackupDirectory);
        var error = Assert.Throws<InvalidOperationException>(() => stale.Initialize());
        Assert.Contains("3", error.Message);

        // 第 5 步：v3 库里老数据原样保留。
        using (var connection = db.Service.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Name FROM Projects;";
            Assert.Equal("迁移演练项目", command.ExecuteScalar());
        }
    }
}
