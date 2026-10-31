using FocusManager.Core.Data;
using FocusManager.Core.Models;
using FocusManager.Core.Time;

namespace FocusManager.Core.Services;

/// <summary>
/// 随手记：专注过程中突然想到的事，先记下来，之后再处理。
/// 记录成本要尽可能低，所以这里只有「加一条」「改状态」「删掉」这几件事，没有标签和优先级。
/// </summary>
public sealed class IdeaNoteService
{
    /// <summary>一条想法最多这么多个字。太长就不像「随手记」了，也避免把界面撑坏。</summary>
    public const int MaxContentLength = 1000;

    private readonly DatabaseService _database;
    private readonly ITimeSource _time;

    public IdeaNoteService(DatabaseService database, ITimeSource? timeSource = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = timeSource ?? new SystemTimeSource();
    }

    /// <summary>
    /// 记下一条想法。
    /// </summary>
    /// <param name="projectId">属于哪个项目。</param>
    /// <param name="content">想法内容，不能为空。</param>
    /// <param name="sessionId">记录时正在进行的专注会话；不在专注中记录就传 null。</param>
    public IdeaNote Add(long projectId, string content, long? sessionId = null)
    {
        var trimmed = (content ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("想法内容不能为空。", nameof(content));
        if (trimmed.Length > MaxContentLength)
            throw new ArgumentException($"一条想法最多 {MaxContentLength} 个字。", nameof(content));

        var now = _time.UtcNow;

        using var connection = _database.OpenConnection();

        var projectExists = Convert.ToInt64(connection.Scalar(
            "SELECT COUNT(*) FROM Projects WHERE Id = $projectId;",
            parameters: [("$projectId", projectId)])) > 0;
        if (!projectExists)
            throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");

        if (sessionId is not null)
        {
            var sessionBelongsToProject = Convert.ToInt64(connection.Scalar(
                "SELECT COUNT(*) FROM FocusSessions WHERE Id = $sessionId AND ProjectId = $projectId;",
                parameters: [("$sessionId", sessionId.Value), ("$projectId", projectId)])) > 0;
            if (!sessionBelongsToProject)
                throw new InvalidOperationException("这条想法要关联的专注记录不属于这个项目。");
        }

        connection.Execute(
            """
            INSERT INTO IdeaNotes (ProjectId, SessionId, Content, Status, CreatedAtUtc, CompletedAtUtc)
            VALUES ($projectId, $sessionId, $content, $status, $createdAt, NULL);
            """,
            parameters:
            [
                ("$projectId", projectId),
                ("$sessionId", sessionId),
                ("$content", trimmed),
                ("$status", IdeaNoteStatus.Pending.ToDbValue()),
                ("$createdAt", SqliteValue.ToDb(now))
            ]);

        return new IdeaNote
        {
            Id = connection.LastInsertRowId(),
            ProjectId = projectId,
            SessionId = sessionId,
            Content = trimmed,
            Status = IdeaNoteStatus.Pending,
            CreatedAtUtc = now,
            CompletedAtUtc = null
        };
    }

    /// <summary>
    /// 列出某个项目的想法，最近记的排在前面。
    /// </summary>
    /// <param name="status">只看某个状态；传 null 表示全部。</param>
    public IReadOnlyList<IdeaNote> ListByProject(long projectId, IdeaNoteStatus? status = null)
    {
        var sql = status is null
            ? """
              SELECT Id, ProjectId, SessionId, Content, Status, CreatedAtUtc, CompletedAtUtc
              FROM IdeaNotes
              WHERE ProjectId = $projectId
              ORDER BY CreatedAtUtc DESC, Id DESC;
              """
            : """
              SELECT Id, ProjectId, SessionId, Content, Status, CreatedAtUtc, CompletedAtUtc
              FROM IdeaNotes
              WHERE ProjectId = $projectId AND Status = $status
              ORDER BY CreatedAtUtc DESC, Id DESC;
              """;

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$projectId", projectId);
        if (status is not null)
            command.Parameters.AddWithValue("$status", status.Value.ToDbValue());

        var notes = new List<IdeaNote>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            notes.Add(Read(reader));

        return notes;
    }

    /// <summary>某个项目下有多少条想法。<paramref name="status"/> 传 null 表示全部。</summary>
    public int CountByProject(long projectId, IdeaNoteStatus? status = null)
    {
        using var connection = _database.OpenConnection();

        return status is null
            ? Convert.ToInt32(connection.Scalar(
                "SELECT COUNT(*) FROM IdeaNotes WHERE ProjectId = $projectId;",
                parameters: [("$projectId", projectId)]))
            : Convert.ToInt32(connection.Scalar(
                "SELECT COUNT(*) FROM IdeaNotes WHERE ProjectId = $projectId AND Status = $status;",
                parameters: [("$projectId", projectId), ("$status", status.Value.ToDbValue())]));
    }

    /// <summary>某一次专注里记了多少条想法（结束总结要用）。</summary>
    public int CountBySession(long sessionId)
    {
        using var connection = _database.OpenConnection();
        return Convert.ToInt32(connection.Scalar(
            "SELECT COUNT(*) FROM IdeaNotes WHERE SessionId = $sessionId;",
            parameters: [("$sessionId", sessionId)]));
    }

    public IdeaNote? FindById(long ideaNoteId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ProjectId, SessionId, Content, Status, CreatedAtUtc, CompletedAtUtc
            FROM IdeaNotes
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", ideaNoteId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>把一条想法改成指定状态。改成「已完成」时记下完成时间，改回「待处理」时清掉。</summary>
    public IdeaNote SetStatus(long ideaNoteId, IdeaNoteStatus status)
    {
        var completedAt = status == IdeaNoteStatus.Done ? _time.UtcNow : (DateTimeOffset?)null;

        using var connection = _database.OpenConnection();
        var affected = connection.Execute(
            "UPDATE IdeaNotes SET Status = $status, CompletedAtUtc = $completedAt WHERE Id = $id;",
            parameters:
            [
                ("$status", status.ToDbValue()),
                ("$completedAt", completedAt is null ? null : SqliteValue.ToDb(completedAt.Value)),
                ("$id", ideaNoteId)
            ]);

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {ideaNoteId} 的想法。");

        return FindById(ideaNoteId)
               ?? throw new InvalidOperationException($"找不到编号为 {ideaNoteId} 的想法。");
    }

    /// <summary>在「待处理」和「已完成」之间切换。</summary>
    public IdeaNote ToggleStatus(long ideaNoteId)
    {
        var note = FindById(ideaNoteId)
                   ?? throw new InvalidOperationException($"找不到编号为 {ideaNoteId} 的想法。");
        var next = note.Status == IdeaNoteStatus.Pending ? IdeaNoteStatus.Done : IdeaNoteStatus.Pending;
        return SetStatus(ideaNoteId, next);
    }

    /// <summary>删掉一条想法。</summary>
    public void Delete(long ideaNoteId)
    {
        using var connection = _database.OpenConnection();
        var affected = connection.Execute(
            "DELETE FROM IdeaNotes WHERE Id = $id;",
            parameters: [("$id", ideaNoteId)]);

        if (affected == 0)
            throw new InvalidOperationException($"找不到编号为 {ideaNoteId} 的想法。");
    }

    private static IdeaNote Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        SessionId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
        Content = reader.GetString(3),
        Status = IdeaNoteStatusExtensions.FromDbValue(reader.GetString(4)),
        CreatedAtUtc = SqliteValue.FromDb(reader.GetString(5)),
        CompletedAtUtc = reader.IsDBNull(6) ? null : SqliteValue.FromDb(reader.GetString(6))
    };
}
