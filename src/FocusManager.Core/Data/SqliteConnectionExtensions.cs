using Microsoft.Data.Sqlite;

namespace FocusManager.Core.Data;

/// <summary>
/// 几个反复用到的小工具，避免每个 Service 都重复写一遍。
/// </summary>
internal static class SqliteConnectionExtensions
{
    /// <summary>取刚插入那一行的自增主键。</summary>
    public static long LastInsertRowId(this SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /// <summary>执行一条不需要返回结果的 SQL。</summary>
    public static int Execute(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    /// <summary>执行一条返回单个值的 SQL。</summary>
    public static object? Scalar(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command.ExecuteScalar();
    }
}
