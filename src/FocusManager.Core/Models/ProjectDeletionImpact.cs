using FocusManager.Core.Time;

namespace FocusManager.Core.Models;

/// <summary>
/// 删除一个项目会连带删掉多少东西——用来在确认对话框里如实告诉用户。
/// </summary>
/// <param name="ProjectId">项目编号。</param>
/// <param name="ProjectName">项目名称。</param>
/// <param name="SessionCount">这个项目下有多少轮专注记录。</param>
/// <param name="TotalEffectiveSeconds">这些专注记录加起来的有效专注秒数。</param>
/// <param name="IdeaNoteCount">这个项目下有多少条随手记想法。</param>
public sealed record ProjectDeletionImpact(
    long ProjectId,
    string ProjectName,
    int SessionCount,
    long TotalEffectiveSeconds,
    int IdeaNoteCount)
{
    /// <summary>累计有效专注时间。</summary>
    public TimeSpan TotalEffectiveTime => TimeSpan.FromSeconds(TotalEffectiveSeconds);

    /// <summary>累计有效专注时间的中文写法，例如「1 小时 25 分钟」。</summary>
    public string TotalEffectiveText => TimeFormatter.ToChinese(TotalEffectiveTime);

    /// <summary>有没有任何会被删掉的学习记录。</summary>
    public bool HasRecords => SessionCount > 0 || IdeaNoteCount > 0;
}
