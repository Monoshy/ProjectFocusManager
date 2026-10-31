using Microsoft.Data.Sqlite;

namespace FocusManager.Core.Data;

/// <summary>
/// 数据库结构的版本升级脚本。
///
/// 规则（很重要，关系到以后历史记录会不会丢）：
/// 1. 已发布的版本不要改，只能往后加新版本；
/// 2. 每个版本用一个独立方法，只做「从上一版到这一版」的增量改动；
/// 3. 版本号写在 SQLite 自带的 PRAGMA user_version 里，程序启动时自动比对。
/// </summary>
public static class Migrations
{
    /// <summary>
    /// 第 1 版：建立全部基础表。
    /// 这一版一次把 7 张核心表 + 1 张心跳表建齐，避免早期频繁改结构。
    /// </summary>
    public static void ApplyV1(SqliteConnection connection, SqliteTransaction transaction)
    {
        const string sql = """
            -- 项目
            CREATE TABLE IF NOT EXISTS Projects (
                Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                Name                  TEXT    NOT NULL,
                Description           TEXT    NULL,
                CreatedAtUtc          TEXT    NOT NULL,
                IsArchived            INTEGER NOT NULL DEFAULT 0,
                TotalEffectiveSeconds INTEGER NOT NULL DEFAULT 0
            );

            -- 已注册的学习应用
            CREATE TABLE IF NOT EXISTS Applications (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT    NOT NULL,
                ExecutablePath TEXT    NOT NULL,
                ProcessName    TEXT    NOT NULL,
                IconPath       TEXT    NULL,
                IsBuiltIn      INTEGER NOT NULL DEFAULT 0,
                IsEnabled      INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc   TEXT    NOT NULL
            );

            -- 专注会话
            CREATE TABLE IF NOT EXISTS FocusSessions (
                Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId            INTEGER NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                PrimaryApplicationId INTEGER NULL     REFERENCES Applications(Id) ON DELETE SET NULL,
                StartedAtUtc         TEXT    NOT NULL,
                EndedAtUtc           TEXT    NULL,
                Status               TEXT    NOT NULL,
                EffectiveSeconds     INTEGER NOT NULL DEFAULT 0,
                PausedSeconds        INTEGER NOT NULL DEFAULT 0,
                PauseCount           INTEGER NOT NULL DEFAULT 0,
                ProgressNote         TEXT    NULL,
                ReflectionNote       TEXT    NULL,
                CreatedAtUtc         TEXT    NOT NULL,
                UpdatedAtUtc         TEXT    NOT NULL
            );

            -- 暂停记录
            CREATE TABLE IF NOT EXISTS PauseRecords (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId       INTEGER NOT NULL REFERENCES FocusSessions(Id) ON DELETE CASCADE,
                Reason          TEXT    NOT NULL,
                StartedAtUtc    TEXT    NOT NULL,
                EndedAtUtc      TEXT    NULL,
                DurationSeconds INTEGER NULL
            );

            -- 有效专注时间段
            CREATE TABLE IF NOT EXISTS FocusIntervals (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId       INTEGER NOT NULL REFERENCES FocusSessions(Id) ON DELETE CASCADE,
                StartedAtUtc    TEXT    NOT NULL,
                EndedAtUtc      TEXT    NOT NULL,
                DurationSeconds INTEGER NOT NULL
            );

            -- 随手记想法
            CREATE TABLE IF NOT EXISTS IdeaNotes (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId      INTEGER NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                SessionId      INTEGER NULL     REFERENCES FocusSessions(Id) ON DELETE SET NULL,
                Content        TEXT    NOT NULL,
                Status         TEXT    NOT NULL DEFAULT 'pending',
                CreatedAtUtc   TEXT    NOT NULL,
                CompletedAtUtc TEXT    NULL
            );

            -- 程序设置（键值对）
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            -- 会话心跳快照（防止异常退出丢时间）
            CREATE TABLE IF NOT EXISTS SessionCheckpoints (
                Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId        INTEGER NOT NULL REFERENCES FocusSessions(Id) ON DELETE CASCADE,
                CapturedAtUtc    TEXT    NOT NULL,
                EffectiveSeconds INTEGER NOT NULL,
                State            TEXT    NOT NULL
            );

            -- 常用查询的索引
            CREATE INDEX IF NOT EXISTS IX_FocusSessions_ProjectId ON FocusSessions(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_FocusSessions_Status    ON FocusSessions(Status);
            CREATE INDEX IF NOT EXISTS IX_FocusIntervals_SessionId ON FocusIntervals(SessionId);
            CREATE INDEX IF NOT EXISTS IX_PauseRecords_SessionId   ON PauseRecords(SessionId);
            CREATE INDEX IF NOT EXISTS IX_IdeaNotes_ProjectId      ON IdeaNotes(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_SessionCheckpoints_SessionId ON SessionCheckpoints(SessionId);
            """;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
