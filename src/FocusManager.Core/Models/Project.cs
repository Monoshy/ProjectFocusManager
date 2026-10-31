namespace FocusManager.Core.Models;

/// <summary>
/// 一个学习项目。用户可以给一个项目做很多次专注，时间会累加。
/// </summary>
public sealed class Project
{
    /// <summary>数据库自增主键。新建时为 0，保存后由数据库赋值。</summary>
    public long Id { get; set; }

    /// <summary>项目名称，例如「微观经济学——消费者理论」。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>可选的备注说明。</summary>
    public string? Description { get; set; }

    /// <summary>创建时间（UTC）。界面显示时再转成本地时间。</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>是否已归档。归档的项目默认不出现在开始专注的候选列表里。</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// 累计有效专注秒数的缓存值。
    /// 注意：真实数据以 FocusIntervals 表求和为准，这个字段只是为了列表显示快，
    /// 每次会话结束后由 ProjectService.RecalculateTotal 刷新。
    /// </summary>
    public long TotalEffectiveSeconds { get; set; }

    public TimeSpan TotalEffectiveTime => TimeSpan.FromSeconds(TotalEffectiveSeconds);

    public override string ToString() => Name;
}
