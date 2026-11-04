using FocusManager.Core.Data;
using FocusManager.Core.Models;
using FocusManager.Core.Time;

namespace FocusManager.Core.Services;

/// <summary>
/// 项目 ↔ 辅助程序的挂载关系。
///
/// 为什么不是「给每个项目单独建一份应用」：
/// 应用注册表（Applications）是全局的，同一个 Obsidian 可能在好几个项目里都要用，
/// 复制成多份会让「改一次名字要改好几处」。所以这里只存关联，程序本身仍然只有一份。
///
/// 这一层管的是「这个项目平时要用哪些程序」；某一轮实际用了哪些，记在会话快照里，
/// 由 <see cref="SessionService"/> 负责。
/// </summary>
public sealed class ProjectApplicationService
{
    private readonly DatabaseService _database;
    private readonly ITimeSource _time;

    public ProjectApplicationService(DatabaseService database, ITimeSource? timeSource = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = timeSource ?? new SystemTimeSource();
    }

    /// <summary>列出这个项目挂着的辅助程序（按添加顺序）。</summary>
    public IReadOnlyList<ApplicationInfo> ListForProject(long projectId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.*
            FROM ProjectApplications pa
            JOIN Applications a ON a.Id = pa.ApplicationId
            WHERE pa.ProjectId = $projectId
            ORDER BY pa.SortOrder, pa.Id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        using var reader = command.ExecuteReader();
        var result = new List<ApplicationInfo>();
        while (reader.Read())
        {
            result.Add(ApplicationService.Map(reader));
        }

        return result;
    }

    /// <summary>列出这个项目挂着的辅助程序编号，用于开始专注时一起快照。</summary>
    public IReadOnlyList<long> ListIdsForProject(long projectId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ApplicationId FROM ProjectApplications
            WHERE ProjectId = $projectId
            ORDER BY SortOrder, Id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        using var reader = command.ExecuteReader();
        var result = new List<long>();
        while (reader.Read())
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }

    /// <summary>这个项目挂着的辅助程序的进程名，用来拼本轮的白名单。</summary>
    public IReadOnlyList<string> ListProcessNamesForProject(long projectId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.ProcessName
            FROM ProjectApplications pa
            JOIN Applications a ON a.Id = pa.ApplicationId
            WHERE pa.ProjectId = $projectId
            ORDER BY pa.SortOrder, pa.Id;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public bool Contains(long projectId, long applicationId)
    {
        using var connection = _database.OpenConnection();
        var value = connection.Scalar(
            "SELECT COUNT(*) FROM ProjectApplications WHERE ProjectId = $projectId AND ApplicationId = $applicationId;",
            null,
            ("$projectId", projectId),
            ("$applicationId", applicationId));

        return value is not null and not DBNull && Convert.ToInt64(value) > 0;
    }

    /// <summary>
    /// 把一个已注册的程序挂到这个项目上。
    /// 已经挂过就什么都不做（返回 false），不会报错——界面上重复点两下不该炸。
    /// </summary>
    public bool Add(long projectId, long applicationId)
    {
        using var connection = _database.OpenConnection();

        var projectExists = connection.Scalar(
            "SELECT COUNT(*) FROM Projects WHERE Id = $id;",
            null,
            ("$id", projectId));

        if (projectExists is null or DBNull || Convert.ToInt64(projectExists) == 0)
            throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");

        var applicationExists = connection.Scalar(
            "SELECT COUNT(*) FROM Applications WHERE Id = $id;",
            null,
            ("$id", applicationId));

        if (applicationExists is null or DBNull || Convert.ToInt64(applicationExists) == 0)
            throw new InvalidOperationException("找不到这个学习应用，请先在应用管理里添加。");

        var existing = connection.Scalar(
            "SELECT COUNT(*) FROM ProjectApplications WHERE ProjectId = $projectId AND ApplicationId = $applicationId;",
            null,
            ("$projectId", projectId),
            ("$applicationId", applicationId));

        if (existing is not null and not DBNull && Convert.ToInt64(existing) > 0)
            return false;

        var nextOrder = connection.Scalar(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM ProjectApplications WHERE ProjectId = $projectId;",
            null,
            ("$projectId", projectId));

        connection.Execute(
            """
            INSERT INTO ProjectApplications (ProjectId, ApplicationId, SortOrder, AddedAtUtc)
            VALUES ($projectId, $applicationId, $sortOrder, $addedAt);
            """,
            parameters:
            [
                ("$projectId", projectId),
                ("$applicationId", applicationId),
                ("$sortOrder", nextOrder is null or DBNull ? 0L : Convert.ToInt64(nextOrder)),
                ("$addedAt", SqliteValue.ToDb(_time.UtcNow))
            ]);

        return true;
    }

    /// <summary>取消挂载。没挂过也不报错。</summary>
    public bool Remove(long projectId, long applicationId)
    {
        using var connection = _database.OpenConnection();
        var affected = connection.Execute(
            "DELETE FROM ProjectApplications WHERE ProjectId = $projectId AND ApplicationId = $applicationId;",
            parameters:
            [
                ("$projectId", projectId),
                ("$applicationId", applicationId)
            ]);

        return affected > 0;
    }

    /// <summary>某个项目挂了多少个辅助程序（界面用来显示「还有没有」）。</summary>
    public int CountForProject(long projectId)
    {
        using var connection = _database.OpenConnection();
        var value = connection.Scalar(
            "SELECT COUNT(*) FROM ProjectApplications WHERE ProjectId = $projectId;",
            null,
            ("$projectId", projectId));

        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }
}
