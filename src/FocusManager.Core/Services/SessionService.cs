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
    private readonly ProjectApplicationService _projectApplications;

    private FocusTimerService? _timer;
    private long _sessionId;
    private long _projectId;

    public SessionService(DatabaseService database, ITimeSource? timeSource = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _time = timeSource ?? new SystemTimeSource();
        _projectApplications = new ProjectApplicationService(_database, _time);
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
    /// <param name="auxiliaryApplicationIds">
    /// 本轮临时要用的辅助程序。项目平时挂着的辅助程序会自动带上，这里只用于
    /// 「项目里还没挂、但这一轮想用」的情况。
    /// </param>
    public long BeginSession(
        long projectId,
        long? primaryApplicationId = null,
        IEnumerable<long>? auxiliaryApplicationIds = null)
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

        using var transaction = connection.BeginTransaction();

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
            transaction,
            parameters:
            [
                ("$projectId", projectId),
                ("$applicationId", primaryApplicationId),
                ("$startedAt", SqliteValue.ToDb(startedAt))
            ]);

        _sessionId = connection.LastInsertRowId(transaction);
        _projectId = projectId;

        // 把「这一轮用了哪些程序」记下来。以后改项目的辅助程序，不会改写历史记录。
        WriteSessionApplications(
            connection,
            transaction,
            _sessionId,
            projectId,
            primaryApplicationId,
            auxiliaryApplicationIds,
            startedAt);

        transaction.Commit();

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

    /// <summary>某一轮专注实际用到的程序（主应用在前，其余按加入顺序）。</summary>
    public IReadOnlyList<SessionApplication> GetSessionApplications(long sessionId)
    {
        using var connection = _database.OpenConnection();
        return ReadSessionApplications(connection, null, sessionId);
    }

    /// <summary>
    /// 专注进行中临时加一个辅助程序：这一轮立刻生效，同时也挂到项目上，
    /// 下次开始这个项目时自动带上。
    /// </summary>
    /// <returns>这一轮的程序快照记录。</returns>
    public SessionApplication AttachAuxiliaryApplication(long applicationId)
    {
        RequireTimer();

        var application = FindApplication(applicationId)
            ?? throw new InvalidOperationException("找不到这个学习应用，请先在应用管理里添加。");

        var sessionId = _sessionId;
        var projectId = _projectId;

        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            var alreadyInSession = connection.Scalar(
                "SELECT COUNT(*) FROM SessionApplications WHERE SessionId = $sessionId AND ApplicationId = $applicationId;",
                transaction,
                ("$sessionId", sessionId),
                ("$applicationId", applicationId));

            if (alreadyInSession is null or DBNull || Convert.ToInt64(alreadyInSession) == 0)
            {
                connection.Execute(
                    """
                    INSERT INTO SessionApplications
                        (SessionId, ApplicationId, NameSnapshot, ProcessNameSnapshot, IsPrimary, AddedAtUtc)
                    VALUES ($sessionId, $applicationId, $name, $processName, 0, $addedAt);
                    """,
                    transaction,
                    parameters:
                    [
                        ("$sessionId", sessionId),
                        ("$applicationId", applicationId),
                        ("$name", application.Name),
                        ("$processName", application.ProcessName),
                        ("$addedAt", SqliteValue.ToDb(_time.UtcNow))
                    ]);
            }

            transaction.Commit();
        }

        // 挂到项目上：下次开始这个项目，它会自动进入本轮的白名单。
        _projectApplications.Add(projectId, applicationId);

        return new SessionApplication
        {
            SessionId = sessionId,
            ApplicationId = applicationId,
            NameSnapshot = application.Name,
            ProcessNameSnapshot = application.ProcessName,
            IsPrimary = false,
            AddedAtUtc = _time.UtcNow
        };
    }

    /// <summary>
    /// 作废当前这一轮：时间不记录，会话连同它的有效段、暂停记录、心跳一起删掉。
    /// 用在「用户主动结束、但有效时间不到 5 分钟」以及用户直接点「作废」的时候。
    /// </summary>
    /// <returns>作废前累计的有效专注秒数（用来提示用户丢掉了多少）。</returns>
    public long DiscardActiveSession()
    {
        var timer = RequireTimer();

        // 结束最后一段（或最后一次暂停），让它先落库，再统计、再删除。
        timer.Stop();

        var sessionId = _sessionId;
        var projectId = _projectId;

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var seconds = SumSessionSeconds(connection, transaction, sessionId);

        DeleteSessionRows(connection, transaction, sessionId);
        ProjectService.RefreshProjectTotal(connection, transaction, projectId);

        transaction.Commit();

        CleanupTimer();

        return seconds;
    }

    /// <summary>
    /// 删除一轮已经结束的专注记录。
    ///
    /// 只删这一轮：项目、想法都留着（想法会变成「不属于任何一轮」，仍然在项目里）。
    /// 正在进行的那一轮不能这样删——先结束或作废，避免界面和数据库对不上。
    /// </summary>
    public SessionDeletionImpact DeleteSession(long sessionId)
    {
        var session = GetSession(sessionId)
            ?? throw new InvalidOperationException($"找不到编号为 {sessionId} 的专注记录。");

        if (session.Status.IsUnfinished())
            throw new InvalidOperationException("这一轮还在进行中，先结束或作废它，再删除记录。");

        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var ideaCount = connection.Scalar(
            "SELECT COUNT(*) FROM IdeaNotes WHERE SessionId = $id;",
            transaction,
            ("$id", sessionId));

        DeleteSessionRows(connection, transaction, sessionId);
        ProjectService.RefreshProjectTotal(connection, transaction, session.ProjectId);

        transaction.Commit();

        return new SessionDeletionImpact(
            sessionId,
            session.ProjectId,
            session.EffectiveSeconds,
            ideaCount is null or DBNull ? 0 : Convert.ToInt32(ideaCount));
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

    /// <summary>把这一轮用到的程序写进快照表：主应用 + 项目挂着的辅助程序 + 临时指定的。</summary>
    private static void WriteSessionApplications(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long sessionId,
        long projectId,
        long? primaryApplicationId,
        IEnumerable<long>? extraApplicationIds,
        DateTimeOffset addedAt)
    {
        var ordered = new List<long>();

        if (primaryApplicationId is { } primary)
            ordered.Add(primary);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT ApplicationId FROM ProjectApplications
                WHERE ProjectId = $projectId
                ORDER BY SortOrder, Id;
                """;
            command.Parameters.AddWithValue("$projectId", projectId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ordered.Add(reader.GetInt64(0));
            }
        }

        if (extraApplicationIds is not null)
        {
            ordered.AddRange(extraApplicationIds);
        }

        var seen = new HashSet<long>();

        foreach (var applicationId in ordered)
        {
            if (!seen.Add(applicationId)) continue;

            var application = FindApplication(connection, transaction, applicationId);

            // 应用可能刚好被删掉：这一条就跳过，历史记录宁可少一条，也不要写个空名字进去。
            if (application is null) continue;

            connection.Execute(
                """
                INSERT INTO SessionApplications
                    (SessionId, ApplicationId, NameSnapshot, ProcessNameSnapshot, IsPrimary, AddedAtUtc)
                VALUES ($sessionId, $applicationId, $name, $processName, $isPrimary, $addedAt);
                """,
                transaction,
                parameters:
                [
                    ("$sessionId", sessionId),
                    ("$applicationId", applicationId),
                    ("$name", application.Name),
                    ("$processName", application.ProcessName),
                    ("$isPrimary", applicationId == primaryApplicationId ? 1 : 0),
                    ("$addedAt", SqliteValue.ToDb(addedAt))
                ]);
        }
    }

    private static IReadOnlyList<SessionApplication> ReadSessionApplications(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long sessionId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT * FROM SessionApplications
            WHERE SessionId = $id
            ORDER BY IsPrimary DESC, Id;
            """;
        command.Parameters.AddWithValue("$id", sessionId);

        using var reader = command.ExecuteReader();
        var result = new List<SessionApplication>();
        while (reader.Read())
        {
            result.Add(new SessionApplication
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                SessionId = reader.GetInt64(reader.GetOrdinal("SessionId")),
                ApplicationId = reader.IsDBNull(reader.GetOrdinal("ApplicationId"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("ApplicationId")),
                NameSnapshot = reader.GetString(reader.GetOrdinal("NameSnapshot")),
                ProcessNameSnapshot = reader.GetString(reader.GetOrdinal("ProcessNameSnapshot")),
                IsPrimary = SqliteValue.FromDbBool(reader.GetInt64(reader.GetOrdinal("IsPrimary"))),
                AddedAtUtc = SqliteValue.FromDb(reader.GetString(reader.GetOrdinal("AddedAtUtc")))
            });
        }

        return result;
    }

    private static void DeleteSessionRows(SqliteConnection connection, SqliteTransaction transaction, long sessionId)
    {
        // 顺序和 ProjectService.Delete 一致：先子表，再主表。
        connection.Execute("DELETE FROM SessionApplications WHERE SessionId = $id;", transaction, ("$id", sessionId));
        connection.Execute("DELETE FROM PauseRecords WHERE SessionId = $id;", transaction, ("$id", sessionId));
        connection.Execute("DELETE FROM FocusIntervals WHERE SessionId = $id;", transaction, ("$id", sessionId));
        connection.Execute("DELETE FROM SessionCheckpoints WHERE SessionId = $id;", transaction, ("$id", sessionId));

        // 想法不删：它属于项目，只是不再挂在这一轮上。
        connection.Execute(
            "UPDATE IdeaNotes SET SessionId = NULL WHERE SessionId = $id;",
            transaction,
            ("$id", sessionId));

        connection.Execute("DELETE FROM FocusSessions WHERE Id = $id;", transaction, ("$id", sessionId));
    }

    private ApplicationInfo? FindApplication(long applicationId)
    {
        using var connection = _database.OpenConnection();
        return FindApplication(connection, null, applicationId);
    }

    private static ApplicationInfo? FindApplication(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long applicationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM Applications WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", applicationId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ApplicationService.Map(reader) : null;
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
