using FocusManager.Core.Data;

namespace FocusManager.Core.Services;

/// <summary>
/// 存放程序自己的小设置（比如用浅色还是深色主题）。
/// 数据存在 AppSettings 表里，是一张简单的「名字 → 值」表。
/// </summary>
public sealed class SettingsService
{
    private readonly DatabaseService _database;

    public SettingsService(DatabaseService database)
        => _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>读一个设置；不存在时返回 null。</summary>
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var connection = _database.OpenConnection();

        var value = connection.Scalar(
            "SELECT Value FROM AppSettings WHERE Key = $key",
            null,
            ("$key", key));

        return value as string;
    }

    /// <summary>写一个设置；已存在就覆盖。</summary>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        using var connection = _database.OpenConnection();

        connection.Execute(
            """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """,
            null,
            ("$key", key),
            ("$value", value));
    }

    /// <summary>读布尔设置，读不到或格式不对时用 <paramref name="fallback"/>。</summary>
    public bool GetBool(string key, bool fallback)
    {
        var raw = Get(key);

        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");
}
