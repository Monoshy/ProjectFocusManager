using FocusManager.Core.Data;
using FocusManager.Core.Models;
using FocusManager.Core.Time;

namespace FocusManager.Core.Services;

/// <summary>
/// 学习应用的注册表管理。
///
/// 说明：应用列表存在数据库里，不是写死在代码里的。
/// 代码里只有一份「首次运行时的预置条目」，之后用户加的应用都来自用户操作。
/// </summary>
public sealed class ApplicationService
{
    /// <summary>
    /// 首次运行时预置的三个应用。路径先留空，用户自己指定 EXE 的位置。
    /// </summary>
    private static readonly (string Name, string ProcessName)[] BuiltInDefaults =
    [
        ("Obsidian", "Obsidian"),
        ("百度网盘", "BaiduNetdisk"),
        ("CCtalk", "CCtalk")
    ];

    private readonly DatabaseService _database;
    private readonly ITimeSource _time;

    public ApplicationService(DatabaseService database, ITimeSource? timeSource = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = timeSource ?? new SystemTimeSource();
    }

    /// <summary>
    /// 补齐预置应用。重复调用不会产生重复记录。
    /// </summary>
    public void EnsureDefaults()
    {
        using var connection = _database.OpenConnection();

        foreach (var (name, processName) in BuiltInDefaults)
        {
            var exists = connection.Scalar(
                "SELECT COUNT(*) FROM Applications WHERE Name = $name AND IsBuiltIn = 1;",
                parameters: [("$name", name)]);

            if (Convert.ToInt64(exists) > 0) continue;

            connection.Execute(
                """
                INSERT INTO Applications (Name, ExecutablePath, ProcessName, IconPath, IsBuiltIn, IsEnabled, CreatedAtUtc)
                VALUES ($name, '', $processName, NULL, 1, 1, $createdAt);
                """,
                parameters:
                [
                    ("$name", name),
                    ("$processName", processName),
                    ("$createdAt", SqliteValue.ToDb(_time.UtcNow))
                ]);
        }
    }

    public IReadOnlyList<ApplicationInfo> List(bool includeDisabled = false)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT * FROM Applications ORDER BY IsBuiltIn DESC, Name;"
            : "SELECT * FROM Applications WHERE IsEnabled = 1 ORDER BY IsBuiltIn DESC, Name;";

        using var reader = command.ExecuteReader();
        var result = new List<ApplicationInfo>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public ApplicationInfo? FindById(long applicationId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Applications WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", applicationId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// 添加一个学习应用。程序文件必须真实存在，否则以后无法用它识别窗口。
    /// </summary>
    public ApplicationInfo Add(string name, string executablePath)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var trimmedPath = (executablePath ?? string.Empty).Trim();

        if (trimmedName.Length == 0)
            throw new ArgumentException("应用名称不能为空。", nameof(name));

        if (trimmedName.Length > 100)
            throw new ArgumentException("应用名称最多 100 个字。", nameof(name));

        if (trimmedPath.Length == 0)
            throw new ArgumentException("请选择程序文件。", nameof(executablePath));

        if (!File.Exists(trimmedPath))
            throw new ArgumentException("找不到这个程序文件，可能已经被移动或删除。", nameof(executablePath));

        var processName = GuessProcessName(trimmedPath);

        using var connection = _database.OpenConnection();

        var duplicate = connection.Scalar(
            "SELECT COUNT(*) FROM Applications WHERE LOWER(ExecutablePath) = LOWER($path);",
            parameters: [("$path", trimmedPath)]);

        if (Convert.ToInt64(duplicate) > 0)
            throw new InvalidOperationException("这个程序已经在应用列表里了。");

        connection.Execute(
            """
            INSERT INTO Applications (Name, ExecutablePath, ProcessName, IconPath, IsBuiltIn, IsEnabled, CreatedAtUtc)
            VALUES ($name, $path, $processName, NULL, 0, 1, $createdAt);
            """,
            parameters:
            [
                ("$name", trimmedName),
                ("$path", trimmedPath),
                ("$processName", processName),
                ("$createdAt", SqliteValue.ToDb(_time.UtcNow))
            ]);

        return FindById(connection.LastInsertRowId())
            ?? throw new InvalidOperationException("添加应用后读取失败。");
    }

    /// <summary>修改已有应用的名称或程序路径。</summary>
    public void Update(long applicationId, string name, string executablePath)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var trimmedPath = (executablePath ?? string.Empty).Trim();

        if (trimmedName.Length == 0)
            throw new ArgumentException("应用名称不能为空。", nameof(name));

        if (trimmedName.Length > 100)
            throw new ArgumentException("应用名称最多 100 个字。", nameof(name));

        if (trimmedPath.Length > 0 && !File.Exists(trimmedPath))
            throw new ArgumentException("找不到这个程序文件，可能已经被移动或删除。", nameof(executablePath));

        using var connection = _database.OpenConnection();

        var existing = FindById(applicationId)
            ?? throw new InvalidOperationException($"找不到编号为 {applicationId} 的应用。");

        // 路径留空时保留原来的进程名：预置条目本来就没有路径，
        // 只改名字不应该把它「认不出是哪个程序」。
        var processName = trimmedPath.Length > 0
            ? GuessProcessName(trimmedPath)
            : existing.ProcessName;

        var affected = connection.Execute(
            """
            UPDATE Applications
            SET Name = $name, ExecutablePath = $path, ProcessName = $processName
            WHERE Id = $id;
            """,
            parameters:
            [
                ("$name", trimmedName),
                ("$path", trimmedPath),
                ("$processName", processName),
                ("$id", applicationId)
            ]);

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {applicationId} 的应用。");
    }

    /// <summary>
    /// 从「当前正在运行的窗口」添加应用。
    ///
    /// 有些程序拿不到可执行文件路径（受保护的系统进程、以管理员权限运行的程序），
    /// 所以这里的路径允许为空——识别窗口靠的是进程名，不是路径。
    /// </summary>
    public ApplicationInfo AddFromRunningProcess(string name, string processName, string? executablePath = null)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        var trimmedProcess = FocusAllowList.Normalize(processName);
        var trimmedPath = (executablePath ?? string.Empty).Trim();

        if (trimmedName.Length == 0)
            throw new ArgumentException("应用名称不能为空。", nameof(name));

        if (trimmedName.Length > 100)
            throw new ArgumentException("应用名称最多 100 个字。", nameof(name));

        if (trimmedProcess.Length == 0)
            throw new ArgumentException("没有拿到这个程序的进程名，无法添加。", nameof(processName));

        if (trimmedPath.Length > 0 && !File.Exists(trimmedPath))
            trimmedPath = string.Empty;

        using var connection = _database.OpenConnection();

        var duplicate = connection.Scalar(
            "SELECT COUNT(*) FROM Applications WHERE LOWER(ProcessName) = LOWER($processName);",
            parameters: [("$processName", trimmedProcess)]);

        if (Convert.ToInt64(duplicate) > 0)
            throw new InvalidOperationException("这个程序已经在应用列表里了。");

        connection.Execute(
            """
            INSERT INTO Applications (Name, ExecutablePath, ProcessName, IconPath, IsBuiltIn, IsEnabled, CreatedAtUtc)
            VALUES ($name, $path, $processName, NULL, 0, 1, $createdAt);
            """,
            parameters:
            [
                ("$name", trimmedName),
                ("$path", trimmedPath),
                ("$processName", trimmedProcess),
                ("$createdAt", SqliteValue.ToDb(_time.UtcNow))
            ]);

        return FindById(connection.LastInsertRowId())
            ?? throw new InvalidOperationException("添加应用后读取失败。");
    }

    public void SetEnabled(long applicationId, bool enabled)
    {
        using var connection = _database.OpenConnection();
        var affected = connection.Execute(
            "UPDATE Applications SET IsEnabled = $enabled WHERE Id = $id;",
            parameters:
            [
                ("$enabled", SqliteValue.ToDb(enabled)),
                ("$id", applicationId)
            ]);

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {applicationId} 的应用。");
    }

    /// <summary>
    /// 删除一个应用。
    /// 历史专注记录不会因此丢失：数据库里对应用是「删除后置空」的关联方式。
    /// </summary>
    public void Delete(long applicationId)
    {
        using var connection = _database.OpenConnection();
        connection.Execute("DELETE FROM Applications WHERE Id = $id;", parameters: [("$id", applicationId)]);
    }

    /// <summary>从程序路径推断进程名（不带 .exe）。</summary>
    public static string GuessProcessName(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;
        return Path.GetFileNameWithoutExtension(executablePath.Trim());
    }

    /// <summary>判断这个应用的程序路径现在还能不能用（用于「路径失效」提示）。</summary>
    public static bool HasUsablePath(ApplicationInfo application)
    {
        if (application is null) return false;
        if (string.IsNullOrWhiteSpace(application.ExecutablePath)) return false;
        return File.Exists(application.ExecutablePath);
    }

    private static ApplicationInfo Map(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        ExecutablePath = reader.GetString(reader.GetOrdinal("ExecutablePath")),
        ProcessName = reader.GetString(reader.GetOrdinal("ProcessName")),
        IconPath = reader.IsDBNull(reader.GetOrdinal("IconPath"))
            ? null
            : reader.GetString(reader.GetOrdinal("IconPath")),
        IsBuiltIn = SqliteValue.FromDbBool(reader.GetInt64(reader.GetOrdinal("IsBuiltIn"))),
        IsEnabled = SqliteValue.FromDbBool(reader.GetInt64(reader.GetOrdinal("IsEnabled"))),
        CreatedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("CreatedAtUtc")))
    };
}
