using FocusManager.Core.Data;
using FocusManager.Core.Models;
using FocusManager.Core.Time;
using Microsoft.Data.Sqlite;

namespace FocusManager.Core.Services;

/// <summary>
/// 专注会话的生命周期管理：开始、暂停、恢复、结束，以及异常退出后的恢复。
///
/// 设计要点：
/// 1. 计时本身交给 <see cref="FocusTimerService"/>（纯内存、单调时钟）。
/// 2. 每完成一段有效时间、每结束一次暂停，立刻写进数据库，崩溃也不丢已完成的段。
/// 3. 所有汇总数字（有效秒数、暂停秒数、暂停次数）都以数据库求和为准，
///    这样即使中途恢复会话、程序重启，统计也不会错。
/// </summary>
public sealed class SessionService
{
    private readonly DatabaseService _database;
    private readonly ITimeSource _time;

    private FocusTimerService? _timer;
    private long _sessionId;
    private long _projectId;

    public SessionService(DatabaseService database, ITimeSource? timeSource = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = timeSource ?? new SystemTimeSource();
    }

    /// <summary>当前是否有一轮专注正在进行（计时中或暂停中）。</summary>
    public bool HasActiveSession => _timer is { IsActive: true };

    public long? CurrentSessionId => HasActiveSession ? _sessionId : null;

    public long? CurrentProjectId => HasActiveSession ? _projectId : null;

    /// <summary>当前的计时器（界面可以读它来刷新显示）。没有会话时为 null。</summary>
    public FocusTimerService? Timer => _timer;

    /// <summary>
    /// 开始一轮新的专注。
    /// </summary>
    /// <param name="projectId">项目编号。</param>
    /// <param name="primaryApplicationId">本轮主要学习应用（可以为空，阶段 2 起会要求必选）。</param>
    public long BeginSession(long projectId, long? primaryApplicationId = null)
    {
        if (HasActiveSession)
            throw new InvalidOperationException("已经有一轮专注正在进行，请先结束它。");

        using var connection = _database.OpenConnection();

        var projectExists = connection.Scalar(
            "SELECT COUNT(*) FROM Projects WHERE Id = $id AND IsArchived = 0;",
            parameters: [("$id", projectId)]);

        if (Convert.ToInt64(projectExists) == 0)
            throw new InvalidOperationException("找不到这个项目，或者它已经被归档了。");

        if (primaryApplicationId is { } applicationId)
        {
            var applicationExists = connection.Scalar(
                "SELECT COUNT(*) FROM Applications WHERE Id = $id;",
                parameters: [("$id", applicationId)]);

            if (Convert.ToInt64(applicationExists) == 0)
                throw new InvalidOperationException("找不到这个学习应用，请先在应用管理里添加。");
        }

        var startedAt = _time.UtcNow;

        connection.Execute(
            """
            INSERT INTO FocusSessions
                (ProjectId, PrimaryApplicationId, StartedAtUtc, EndedAtUtc, Status,
                 EffectiveSeconds, PausedSeconds, PauseCount, ProgressNote, ReflectionNote,
                 CreatedAtUtc, UpdatedAtUtc)
            VALUES
                ($projectId, $applicationId, $startedAt, NULL, 'running',
                 0, 0, 0, NULL, NULL, $startedAt, $startedAt);
            """,
            parameters:
            [
                ("$projectId", projectId),
                ("$applicationId", primaryApplicationId),
                ("$startedAt", SqliteValue.ToDb(startedAt))
            ]);

        _sessionId = connection.LastInsertRowId();
        _projectId = projectId;

        _timer = new FocusTimerService(_time);
        _timer.IntervalCompleted += OnIntervalCompleted;
        _timer.PauseCompleted += OnPauseCompleted;
        _timer.Start();

        return _sessionId;
    }

    /// <summary>
    /// 暂停。原因必须填写（界面会先弹窗让用户选或输入）。
    /// 暂停后不会自动恢复，也不会自动结束。
    /// </summary>
    public void PauseSession(string reason)
    {
        var timer = RequireTimer();

        if (timer.IsPaused)
            throw new InvalidOperationException("已经处于暂停状态。");

        // 这一步会结束当前有效专注段，并触发 OnIntervalCompleted 立即落库。
        timer.Pause(reason);

        using var connection = _database.OpenConnection();

        connection.Execute(
            """
            INSERT INTO PauseRecords (SessionId, Reason, StartedAtUtc, EndedAtUtc, DurationSeconds)
            VALUES ($sessionId, $reason, $startedAt, NULL, NULL);
            """,
            parameters:
            [
                ("$sessionId", _sessionId),
                ("$reason", timer.CurrentPauseReason),
                ("$startedAt", SqliteValue.ToDb(timer.CurrentPauseStartedAtUtc))
            ]);

        connection.Execute(
            "UPDATE FocusSessions SET Status = 'paused', UpdatedAtUtc = $now WHERE Id = $id;",
            parameters:
            [
                ("$now", SqliteValue.ToDb(_time.UtcNow)),
                ("$id", _sessionId)
            ]);
    }

    /// <summary>从暂停恢复计时，开始新的一段有效专注时间。</summary>
    public void ResumeSession()
    {
        var timer = RequireTimer();

        if (!timer.IsPaused)
            throw new InvalidOperationException("当前不处于暂停状态。");

        // 这一步会结束当前暂停，并触发 OnPauseCompleted 更新数据库里的暂停记录。
        timer.Resume();

        using var connection = _database.OpenConnection();
        connection.Execute(
            "UPDATE FocusSessions SET Status = 'running', UpdatedAtUtc = $now WHERE Id = $id;",
            parameters:
            [
                ("$now", SqliteValue.ToDb(_time.UtcNow)),
                ("$id", _sessionId)
            ]);
    }

    /// <summary>
    /// 冻结计时。用在「用户点了结束、正在总结窗口里写内容」这段时间：
    /// 写总结的时间不应该被算成专注时间，所以先把时间停住。
    /// 如果用户在总结窗口里点了「继续专注」，调用 <see cref="UnfreezeTimerAfterSummary"/>。
    /// </summary>
    public void FreezeTimerForSummary()
    {
        _timer?.Freeze();
    }

    /// <summary>解除冻结，从当前时刻重新开始计时（冻结期间不计入）。</summary>
    public void UnfreezeTimerAfterSummary()
    {
        _timer?.Unfreeze();
    }

    /// <summary>
    /// 正常结束本次专注，保存总结。
    /// </summary>
    /// <param name="progressNote">本轮完成了什么（必填）。</param>
    /// <param name="reflectionNote">心得或未解决的问题（选填）。</param>
    public FocusSession CompleteSession(string progressNote, string? reflectionNote = null)
    {
        if (string.IsNullOrWhiteSpace(progressNote))
            throw new ArgumentException("必须填写「本轮完成了什么」。", nameof(progressNote));

        return FinishSession(SessionStatus.Completed, progressNote, reflectionNote);
    }

    /// <summary>
    /// 异常/强制结束（例如用户直接关掉程序）。已完成的专注时间照样保留。
    /// </summary>
    public FocusSession AbortSession(string? note = null)
        => FinishSession(SessionStatus.Aborted, note, null);

    private FocusSession FinishSession(SessionStatus finalStatus, string? progressNote, string? reflectionNote)
    {
        var timer = RequireTimer();

        // 结束最后一段（或最后一次暂停），触发落库。
        // 注意：结束时间必须取计时器算出来的那个值，不能重新读一次系统时间。
        // 因为用户可能是「点了结束、在总结窗口里写了半分钟」才保存的，
        // 计时器会把结束时间停在按「结束」的那一刻，重新读时间就会把写总结的时间算进去。
        var stopped = timer.Stop();
        var endedAt = stopped.EndedAtUtc;

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            """
            UPDATE FocusSessions
            SET EndedAtUtc      = $endedAt,
                Status          = $status,
                ProgressNote    = $progress,
                ReflectionNote  = $reflection,
                UpdatedAtUtc    = $now
            WHERE Id = $id;
            """,
            transaction,
            ("$endedAt", SqliteValue.ToDb(endedAt)),
            ("$status", finalStatus.ToDbValue()),
            ("$progress", string.IsNullOrWhiteSpace(progressNote) ? null : progressNote.Trim()),
            ("$reflection", string.IsNullOrWhiteSpace(reflectionNote) ? null : reflectionNote.Trim()),
            ("$now", SqliteValue.ToDb(endedAt)),
            ("$id", _sessionId));

        RefreshTotals(connection, transaction, _sessionId);
        ProjectService.RefreshProjectTotal(connection, transaction, _projectId);
        connection.Execute("DELETE FROM SessionCheckpoints WHERE SessionId = $id;", transaction, ("$id", _sessionId));

        transaction.Commit();

        var session = ReadSession(connection, null, _sessionId)
            ?? throw new InvalidOperationException("保存会话后读取失败。");

        CleanupTimer();
        return session;
    }

    /// <summary>
    /// 保存一次心跳。界面每隔十几秒调用一次即可。
    /// 作用：万一程序被强制结束，能知道「最后保存到什么时候」。
    /// </summary>
    public void SaveCheckpoint()
    {
        if (!HasActiveSession) return;

        // 先把「当前这一段」切下来存进数据库，这样即使下一秒程序被强制结束，
        // 已经专注的时间也不会丢。最短 30 秒才切一次，避免产生大量碎片段。
        _timer!.FlushCurrentInterval(TimeSpan.FromSeconds(30));

        using var connection = _database.OpenConnection();

        var seconds = SumSessionSeconds(connection, null, _sessionId);
        var state = _timer!.IsPaused ? "paused" : "running";

        connection.Execute(
            """
            INSERT INTO SessionCheckpoints (SessionId, CapturedAtUtc, EffectiveSeconds, State)
            VALUES ($sessionId, $capturedAt, $seconds, $state);
            """,
            parameters:
            [
                ("$sessionId", _sessionId),
                ("$capturedAt", SqliteValue.ToDb(_time.UtcNow)),
                ("$seconds", seconds),
                ("$state", state)
            ]);

        // 只保留最近 50 条，避免表无限变大。
        connection.Execute(
            """
            DELETE FROM SessionCheckpoints
            WHERE SessionId = $id
              AND Id NOT IN (
                  SELECT Id FROM SessionCheckpoints WHERE SessionId = $id ORDER BY Id DESC LIMIT 50
              );
            """,
            parameters: [("$id", _sessionId)]);
    }

    /// <summary>
    /// 找出上次没有正常结束的会话（程序崩溃、被强制关闭时会留下）。
    /// 启动时调用，界面据此询问用户「恢复」还是「结束」。
    /// </summary>
    public IReadOnlyList<FocusSession> GetUnfinishedSessions()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM FocusSessions
            WHERE Status IN ('running', 'paused')
            ORDER BY StartedAtUtc DESC;
            """;

        using var reader = command.ExecuteReader();
        var result = new List<FocusSession>();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }

        return result;
    }

    /// <summary>
    /// 把上次未结束的会话「按已保存的内容结束掉」。
    /// 关键：程序关闭期间的时间不会被算成专注时间，因为那段时间根本没有产生有效段。
    /// </summary>
    public FocusSession FinishUnfinishedSession(long sessionId)
    {
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var session = ReadSession(connection, transaction, sessionId)
            ?? throw new InvalidOperationException($"找不到编号为 {sessionId} 的会话。");

        if (!session.Status.IsUnfinished())
            throw new InvalidOperationException("这个会话已经结束过了。");

        // 结束时间取最后一条心跳；没有心跳就退回到会话开始时间（保守，绝不虚增时间）。
        var endedAt = ReadLastCheckpointTime(connection, transaction, sessionId) ?? session.StartedAtUtc;

        // 悬挂的暂停记录补上结束时间，时长记 0：关闭期间既不算专注，也无法确认是暂停。
        connection.Execute(
            """
            UPDATE PauseRecords
            SET EndedAtUtc = $endedAt, DurationSeconds = 0
            WHERE SessionId = $id AND EndedAtUtc IS NULL;
            """,
            transaction,
            ("$endedAt", SqliteValue.ToDb(endedAt)),
            ("$id", sessionId));

        connection.Execute(
            """
            UPDATE FocusSessions
            SET EndedAtUtc = $endedAt, Status = 'recovered', UpdatedAtUtc = $now
            WHERE Id = $id;
            """,
            transaction,
            ("$endedAt", SqliteValue.ToDb(endedAt)),
            ("$now", SqliteValue.ToDb(_time.UtcNow)),
            ("$id", sessionId));

        RefreshTotals(connection, transaction, sessionId);
        ProjectService.RefreshProjectTotal(connection, transaction, session.ProjectId);
        connection.Execute("DELETE FROM SessionCheckpoints WHERE SessionId = $id;", transaction, ("$id", sessionId));

        transaction.Commit();

        return ReadSession(connection, null, sessionId)!;
    }

    /// <summary>
    /// 恢复上次未结束的会话：从「现在」继续计时，不补算程序关闭期间的时间。
    /// </summary>
    public FocusSession ResumeUnfinishedSession(long sessionId)
    {
        if (HasActiveSession)
            throw new InvalidOperationException("已经有一轮专注正在进行，请先结束它。");

        var session = GetSession(sessionId)
            ?? throw new InvalidOperationException($"找不到编号为 {sessionId} 的会话。");

        if (!session.Status.IsUnfinished())
            throw new InvalidOperationException("这个会话已经结束过了。");

        _sessionId = sessionId;
        _projectId = session.ProjectId;

        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            var now = _time.UtcNow;

            connection.Execute(
                """
                UPDATE PauseRecords
                SET EndedAtUtc = $now, DurationSeconds = 0
                WHERE SessionId = $id AND EndedAtUtc IS NULL;
                """,
                transaction,
                ("$now", SqliteValue.ToDb(now)),
                ("$id", sessionId));

            connection.Execute(
                "UPDATE FocusSessions SET Status = 'running', UpdatedAtUtc = $now WHERE Id = $id;",
                transaction,
                ("$now", SqliteValue.ToDb(now)),
                ("$id", sessionId));

            RefreshTotals(connection, transaction, sessionId);
            transaction.Commit();
        }

        _timer = new FocusTimerService(_time);
        _timer.IntervalCompleted += OnIntervalCompleted;
        _timer.PauseCompleted += OnPauseCompleted;
        _timer.Start();

        return GetSession(sessionId)!;
    }

    public FocusSession? GetSession(long sessionId)
    {
        using var connection = _database.OpenConnection();
        return ReadSession(connection, null, sessionId);
    }

    public IReadOnlyList<FocusInterval> GetIntervals(long sessionId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FocusIntervals WHERE SessionId = $id ORDER BY StartedAtUtc;";
        command.Parameters.AddWithValue("$id", sessionId);

        using var reader = command.ExecuteReader();
        var result = new List<FocusInterval>();
        while (reader.Read())
        {
            result.Add(new FocusInterval
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                SessionId = reader.GetInt64(reader.GetOrdinal("SessionId")),
                StartedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("StartedAtUtc"))),
                EndedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("EndedAtUtc"))),
                DurationSeconds = reader.GetInt64(reader.GetOrdinal("DurationSeconds"))
            });
        }

        return result;
    }

    public IReadOnlyList<PauseRecord> GetPauses(long sessionId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM PauseRecords WHERE SessionId = $id ORDER BY StartedAtUtc;";
        command.Parameters.AddWithValue("$id", sessionId);

        using var reader = command.ExecuteReader();
        var result = new List<PauseRecord>();
        while (reader.Read())
        {
            result.Add(new PauseRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                SessionId = reader.GetInt64(reader.GetOrdinal("SessionId")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                StartedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("StartedAtUtc"))),
                EndedAtUtc = reader.IsDBNull(reader.GetOrdinal("EndedAtUtc"))
                    ? null
                    : SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("EndedAtUtc"))),
                DurationSeconds = reader.IsDBNull(reader.GetOrdinal("DurationSeconds"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("DurationSeconds"))
            });
        }

        return result;
    }

    /// <summary>从数据库求和，得到某个会话的有效专注秒数。</summary>
    public long GetSessionEffectiveSeconds(long sessionId)
    {
        using var connection = _database.OpenConnection();
        return SumSessionSeconds(connection, null, sessionId);
    }

    private void OnIntervalCompleted(object? sender, FocusIntervalSnapshot snapshot)
    {
        if (_sessionId == 0) return;

        using var connection = _database.OpenConnection();

        connection.Execute(
            """
            INSERT INTO FocusIntervals (SessionId, StartedAtUtc, EndedAtUtc, DurationSeconds)
            VALUES ($sessionId, $startedAt, $endedAt, $duration);
            """,
            parameters:
            [
                ("$sessionId", _sessionId),
                ("$startedAt", SqliteValue.ToDb(snapshot.StartedAtUtc)),
                ("$endedAt", SqliteValue.ToDb(snapshot.EndedAtUtc)),
                ("$duration", ToWholeSeconds(snapshot.Duration))
            ]);

        RefreshTotals(connection, null, _sessionId);
    }

    private void OnPauseCompleted(object? sender, PauseSnapshot snapshot)
    {
        if (_sessionId == 0) return;

        using var connection = _database.OpenConnection();

        connection.Execute(
            """
            UPDATE PauseRecords
            SET EndedAtUtc = $endedAt, DurationSeconds = $duration
            WHERE Id = (
                SELECT Id FROM PauseRecords
                WHERE SessionId = $sessionId AND EndedAtUtc IS NULL
                ORDER BY Id DESC LIMIT 1
            );
            """,
            parameters:
            [
                ("$endedAt", SqliteValue.ToDb(snapshot.EndedAtUtc)),
                ("$duration", ToWholeSeconds(snapshot.Duration)),
                ("$sessionId", _sessionId)
            ]);

        RefreshTotals(connection, null, _sessionId);
    }

    private static long ToWholeSeconds(TimeSpan value)
        => (long)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);

    private static long SumSessionSeconds(SqliteConnection connection, SqliteTransaction? transaction, long sessionId)
    {
        var value = connection.Scalar(
            "SELECT COALESCE(SUM(DurationSeconds), 0) FROM FocusIntervals WHERE SessionId = $id;",
            transaction,
            ("$id", sessionId));

        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private void RefreshTotals(SqliteConnection connection, SqliteTransaction? transaction, long sessionId)
    {
        connection.Execute(
            """
            UPDATE FocusSessions
            SET EffectiveSeconds = (SELECT COALESCE(SUM(DurationSeconds), 0) FROM FocusIntervals WHERE SessionId = $id),
                PausedSeconds    = (SELECT COALESCE(SUM(DurationSeconds), 0) FROM PauseRecords WHERE SessionId = $id),
                PauseCount       = (SELECT COUNT(*) FROM PauseRecords WHERE SessionId = $id),
                UpdatedAtUtc     = $now
            WHERE Id = $id;
            """,
            transaction,
            ("$id", sessionId),
            ("$now", SqliteValue.ToDb(_time.UtcNow)));
    }

    private static DateTimeOffset? ReadLastCheckpointTime(SqliteConnection connection, SqliteTransaction? transaction, long sessionId)
    {
        var value = connection.Scalar(
            "SELECT CapturedAtUtc FROM SessionCheckpoints WHERE SessionId = $id ORDER BY Id DESC LIMIT 1;",
            transaction,
            ("$id", sessionId));

        return value is null or DBNull ? null : SqliteValue.FromDb((string)value);
    }

    private static FocusSession? ReadSession(SqliteConnection connection, SqliteTransaction? transaction, long sessionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM FocusSessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", sessionId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static FocusSession Map(SqliteDataReader reader) => new()
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
    };

    private FocusTimerService RequireTimer()
        => _timer ?? throw new InvalidOperationException("当前没有正在进行的专注。");

    private void CleanupTimer()
    {
        if (_timer is not null)
        {
            _timer.IntervalCompleted -= OnIntervalCompleted;
            _timer.PauseCompleted -= OnPauseCompleted;
        }

        _timer = null;
        _sessionId = 0;
        _projectId = 0;
    }
}
