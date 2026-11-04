using FocusManager.Core.Data;
using FocusManager.Core.Models;
using FocusManager.Core.Time;

namespace FocusManager.Core.Services;

/// <summary>
/// 项目管理：创建、查询、改名、归档，以及累计专注时间的统计。
/// </summary>
public sealed class ProjectService
{
    private readonly DatabaseService _database;
    private readonly ITimeSource _time;

    public ProjectService(DatabaseService database, ITimeSource? timeSource = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = timeSource ?? new SystemTimeSource();
    }

    /// <summary>新建项目。名称不能为空。</summary>
    public Project Create(string name, string? description = null)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
            throw new ArgumentException("项目名称不能为空。", nameof(name));
        if (trimmedName.Length > 200)
            throw new ArgumentException("项目名称最多 200 个字。", nameof(name));

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        var now = _time.UtcNow;

        using var connection = _database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO Projects (Name, Description, CreatedAtUtc, IsArchived, TotalEffectiveSeconds)
            VALUES ($name, $description, $createdAt, 0, 0);
            """,
            parameters:
            [
                ("$name", trimmedName),
                ("$description", trimmedDescription),
                ("$createdAt", SqliteValue.ToDb(now))
            ]);

        var id = connection.LastInsertRowId();

        return new Project
        {
            Id = id,
            Name = trimmedName,
            Description = trimmedDescription,
            CreatedAtUtc = now,
            IsArchived = false,
            TotalEffectiveSeconds = 0
        };
    }

    /// <summary>列出项目。默认不包含已归档的。</summary>
    public IReadOnlyList<Project> List(bool includeArchived = false)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = includeArchived
            ? "SELECT * FROM Projects ORDER BY IsArchived, CreatedAtUtc DESC;"
            : "SELECT * FROM Projects WHERE IsArchived = 0 ORDER BY CreatedAtUtc DESC;";

        using var reader = command.ExecuteReader();
        var result = new List<Project>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public Project? FindById(long id)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Projects WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>按名称模糊查找（用于界面上输入时快速定位）。</summary>
    public IReadOnlyList<Project> Search(string keyword)
    {
        var trimmed = (keyword ?? string.Empty).Trim();

        // 空关键字不当作「搜索全部」，避免界面上一清空输入框就刷出所有项目。
        if (trimmed.Length == 0) return Array.Empty<Project>();

        // 用户输入里的 % 和 _ 要当普通字符处理，否则会意外匹配到别的项目。
        var escaped = trimmed
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        var pattern = $"%{escaped}%";

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Projects WHERE IsArchived = 0 AND Name LIKE $pattern ESCAPE '\\' ORDER BY CreatedAtUtc DESC;";
        command.Parameters.AddWithValue("$pattern", pattern);

        using var reader = command.ExecuteReader();
        var result = new List<Project>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public void Rename(long projectId, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("项目名称不能为空。", nameof(newName));
        if (trimmed.Length > 200)
            throw new ArgumentException("项目名称最多 200 个字。", nameof(newName));

        using var connection = _database.OpenConnection();
        var affected = connection.Execute(
            "UPDATE Projects SET Name = $name WHERE Id = $id;",
            parameters: [("$name", trimmed), ("$id", projectId)]);

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");
    }

    /// <summary>归档或取消归档。归档等于「收起来」，数据不会删除。</summary>
    public void SetArchived(long projectId, bool archived)
    {
        using var connection = _database.OpenConnection();
        var affected = connection.Execute(
            "UPDATE Projects SET IsArchived = $archived WHERE Id = $id;",
            parameters: [("$archived", archived ? 1 : 0), ("$id", projectId)]);

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");
    }

    /// <summary>
    /// 删除前先看一眼会连带删掉什么，好让用户知道自己在做什么。
    /// 只读，不修改任何数据。
    /// </summary>
    public ProjectDeletionImpact GetDeletionImpact(long projectId)
    {
        using var connection = _database.OpenConnection();

        var name = connection.Scalar(
            "SELECT Name FROM Projects WHERE Id = $id;",
            null,
            ("$id", projectId));

        if (name is null or DBNull)
            throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");

        var sessionCount = connection.Scalar(
            "SELECT COUNT(*) FROM FocusSessions WHERE ProjectId = $id;",
            null,
            ("$id", projectId));

        var ideaCount = connection.Scalar(
            "SELECT COUNT(*) FROM IdeaNotes WHERE ProjectId = $id;",
            null,
            ("$id", projectId));

        return new ProjectDeletionImpact(
            projectId,
            Convert.ToString(name) ?? string.Empty,
            sessionCount is null or DBNull ? 0 : Convert.ToInt32(sessionCount),
            SumProjectSeconds(connection, null, projectId),
            ideaCount is null or DBNull ? 0 : Convert.ToInt32(ideaCount));
    }

    /// <summary>
    /// 彻底删除一个项目，连带它的所有专注会话、有效时间段、暂停记录和想法。
    ///
    /// 为什么不是「归档」：归档只是收起来，列表里看不见但数据还在。
    /// 删除是用户明确要求的行为，所以必须删干净，否则数据库里会留下找不到主人的孤儿记录。
    ///
    /// 还有一轮专注没结束时不允许删除——那一轮还在计时，删掉会让程序状态错乱。
    /// </summary>
    public ProjectDeletionImpact Delete(long projectId)
    {
        var impact = GetDeletionImpact(projectId);

        using var connection = _database.OpenConnection();

        var unfinished = connection.Scalar(
            "SELECT COUNT(*) FROM FocusSessions WHERE ProjectId = $id AND Status IN ('running', 'paused');",
            null,
            ("$id", projectId));

        if (unfinished is not null and not DBNull && Convert.ToInt32(unfinished) > 0)
            throw new InvalidOperationException("这个项目还有一轮专注没有结束，请先结束它再删除。");

        using var transaction = connection.BeginTransaction();

        // 顺序很重要：先删子表再删主表。数据库本身也配了 ON DELETE CASCADE，
        // 但这里显式删一遍，不依赖 PRAGMA foreign_keys 是否被意外关掉。
        connection.Execute(
            """
            DELETE FROM PauseRecords
            WHERE SessionId IN (SELECT Id FROM FocusSessions WHERE ProjectId = $id);
            """,
            transaction,
            ("$id", projectId));

        connection.Execute(
            """
            DELETE FROM FocusIntervals
            WHERE SessionId IN (SELECT Id FROM FocusSessions WHERE ProjectId = $id);
            """,
            transaction,
            ("$id", projectId));

        connection.Execute(
            """
            DELETE FROM SessionCheckpoints
            WHERE SessionId IN (SELECT Id FROM FocusSessions WHERE ProjectId = $id);
            """,
            transaction,
            ("$id", projectId));

        connection.Execute(
            "DELETE FROM FocusSessions WHERE ProjectId = $id;",
            transaction,
            ("$id", projectId));

        connection.Execute(
            "DELETE FROM IdeaNotes WHERE ProjectId = $id;",
            transaction,
            ("$id", projectId));

        // 项目挂了哪些辅助程序，也跟着一起清掉（数据库本身也配了级联，这里显式写一遍）。
        connection.Execute(
            "DELETE FROM ProjectApplications WHERE ProjectId = $id;",
            transaction,
            ("$id", projectId));

        var affected = connection.Execute(
            "DELETE FROM Projects WHERE Id = $id;",
            transaction,
            ("$id", projectId));

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");

        transaction.Commit();

        return impact;
    }

    /// <summary>
    /// 计算项目累计有效专注秒数。
    /// 这是「唯一真相」：直接对所有会话的有效段求和，不依赖缓存字段。
    /// </summary>
    public long GetTotalEffectiveSeconds(long projectId)
    {
        using var connection = _database.OpenConnection();
        return SumProjectSeconds(connection, null, projectId);
    }

    /// <summary>
    /// 把累计时间刷新到 Projects.TotalEffectiveSeconds 缓存字段。
    /// 每次会话结束后调用，列表显示就不用每次都做汇总查询。
    /// </summary>
    public long RecalculateTotal(long projectId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var total = RefreshProjectTotal(connection, transaction, projectId);
        transaction.Commit();
        return total;
    }

    internal static long SumProjectSeconds(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, long projectId)
    {
        var value = connection.Scalar(
            """
            SELECT COALESCE(SUM(fi.DurationSeconds), 0)
            FROM FocusIntervals fi
            JOIN FocusSessions fs ON fs.Id = fi.SessionId
            WHERE fs.ProjectId = $projectId;
            """,
            transaction,
            ("$projectId", projectId));

        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    internal static long RefreshProjectTotal(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction? transaction, long projectId)
    {
        var total = SumProjectSeconds(connection, transaction, projectId);
        connection.Execute(
            "UPDATE Projects SET TotalEffectiveSeconds = $total WHERE Id = $id;",
            transaction,
            ("$total", total),
            ("$id", projectId));

        return total;
    }

    private static Project Map(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
        CreatedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))),
        IsArchived = reader.GetInt64(reader.GetOrdinal("IsArchived")) != 0,
        TotalEffectiveSeconds = reader.GetInt64(reader.GetOrdinal("TotalEffectiveSeconds"))
    };
}
