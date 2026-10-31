using System.Globalization;

namespace FocusManager.Core.Data;

/// <summary>
/// 数据库里时间字段的读写规则。
///
/// 统一约定：所有时间都存成 UTC 的 ISO-8601 文本，例如
///   2026-10-31T06:43:58.1234567Z
/// 这样做的原因：
/// 1. 存 UTC 不会因为时区、夏令时产生歧义；
/// 2. 文本格式是定长的，SQLite 里排序结果就是时间顺序；
/// 3. 显示给用户时再转成本地时间（TimeFormatter 负责）。
/// </summary>
public static class SqliteValue
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string ToDb(DateTimeOffset value)
        => value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

    public static string? ToDb(DateTimeOffset? value)
        => value is null ? null : ToDb(value.Value);

    public static DateTimeOffset FromDb(string text)
        => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? FromDbNullable(string? text)
        => string.IsNullOrEmpty(text) ? null : FromDb(text);

    public static long ToDb(bool value) => value ? 1 : 0;

    public static bool FromDbBool(long value) => value != 0;
}
