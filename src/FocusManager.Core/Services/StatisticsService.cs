using FocusManager.Core.Data;
using FocusManager.Core.Models;

namespace FocusManager.Core.Services;

/// <summary>某个项目的统计概览。</summary>
public sealed record ProjectStatistics(
    Project Project,
    long TotalEffectiveSeconds,
    int SessionCount,
    DateTimeOffset? LastSessionAtUtc,
    int IdeaCount)
{
    public TimeSpan TotalEffectiveTime => TimeSpan.FromSeconds(TotalEffectiveSeconds);
}

/// <summary>某一天的专注合计（按用户本地日期算）。</summary>
public sealed record DailyTotal(DateOnly Date, long EffectiveSeconds, int SessionCount)
{
    public TimeSpan EffectiveTime => TimeSpan.FromSeconds(EffectiveSeconds);
}

/// <summary>
/// 统计查询。只负责「读出来算给你看」，不修改任何数据。
/// </summary>
public sealed class StatisticsService
{
    private readonly DatabaseService _database;

    public StatisticsService(DatabaseService database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>项目的统计概览。</summary>
    public ProjectStatistics GetProjectStatistics(long projectId)
    {
        var project = new ProjectService(_database).FindById(projectId)
            ?? throw new InvalidOperationException($"找不到编号为 {projectId} 的项目。");

        using var connection = _database.OpenConnection();

        var total = ProjectService.SumProjectSeconds(connection, null, projectId);

        var sessionCount = Convert.ToInt32(connection.Scalar(
            "SELECT COUNT(*) FROM FocusSessions WHERE ProjectId = $id AND Status IN ('completed','aborted','recovered');",
            parameters: [("$id", projectId)]));

        var lastSessionRaw = connection.Scalar(
            "SELECT MAX(StartedAtUtc) FROM FocusSessions WHERE ProjectId = $id;",
            parameters: [("$id", projectId)]);

        var lastSession = lastSessionRaw is null or DBNull
            ? (DateTimeOffset?)null
            : SqliteValue.FromDb((string)lastSessionRaw);

        var ideaCount = Convert.ToInt32(connection.Scalar(
            "SELECT COUNT(*) FROM IdeaNotes WHERE ProjectId = $id;",
            parameters: [("$id", projectId)]));

        return new ProjectStatistics(project, total, sessionCount, lastSession, ideaCount);
    }

    /// <summary>
    /// 按本地日期汇总每天的专注时间。
    /// 说明：一段跨过午夜的时间会整段算在「开始的那一天」，这对日常查看足够准确。
    /// </summary>
    public IReadOnlyList<DailyTotal> GetDailyTotals(long projectId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fi.StartedAtUtc, fi.DurationSeconds, fi.SessionId
            FROM FocusIntervals fi
            JOIN FocusSessions fs ON fs.Id = fi.SessionId
            WHERE fs.ProjectId = $projectId
            ORDER BY fi.StartedAtUtc;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        var buckets = new Dictionary<DateOnly, (long Seconds, HashSet<long> Sessions)>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var startedAt = SqliteValue.FromDb(reader.GetString(0));
            var seconds = reader.GetInt64(1);
            var sessionId = reader.GetInt64(2);

            var localDate = DateOnly.FromDateTime(startedAt.ToLocalTime().DateTime);

            if (!buckets.TryGetValue(localDate, out var bucket))
            {
                bucket = (0, new HashSet<long>());
            }

            bucket.Seconds += seconds;
            bucket.Sessions.Add(sessionId);
            buckets[localDate] = bucket;
        }

        return buckets
            .OrderByDescending(pair => pair.Key)
            .Select(pair => new DailyTotal(pair.Key, pair.Value.Seconds, pair.Value.Sessions.Count))
            .ToArray();
    }

    /// <summary>按时间倒序列出项目的专注会话。</summary>
    public IReadOnlyList<FocusSession> GetSessions(long projectId, int limit = 100)
    {
        if (limit <= 0) limit = 100;

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM FocusSessions
            WHERE ProjectId = $projectId
            ORDER BY StartedAtUtc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var result = new List<FocusSession>();
        while (reader.Read())
        {
            result.Add(new FocusSession
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                ProjectId = reader.GetInt64(reader.GetOrdinal("ProjectId")),
                PrimaryApplicationId = reader.IsDBNull(reader.GetOrdinal("PrimaryApplicationId"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("PrimaryApplicationId")),
                StartedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("StartedAtUtc"))),
                EndedAtUtc = reader.IsDBNull(reader.GetOrdinal("EndedAtUtc"))
                    ? null
                    : SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("EndedAtUtc"))),
                Status = SessionStatusExtensions.FromDbValue(reader.GetString(reader.GetOrdinal("Status"))),
                EffectiveSeconds = reader.GetInt64(reader.GetOrdinal("EffectiveSeconds")),
                PausedSeconds = reader.GetInt64(reader.GetOrdinal("PausedSeconds")),
                PauseCount = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("PauseCount"))),
                ProgressNote = reader.IsDBNull(reader.GetOrdinal("ProgressNote"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ProgressNote")),
                ReflectionNote = reader.IsDBNull(reader.GetOrdinal("ReflectionNote"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ReflectionNote")),
                CreatedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("CreatedAtUtc"))),
                UpdatedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("UpdatedAtUtc")))
            });
        }

        return result;
    }
}
