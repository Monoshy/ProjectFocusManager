using FocusManager.Core.Time;

namespace FocusManager.Core.Models;

/// <summary>
/// 删除一轮专注记录会连带删掉什么——用来在确认框里如实告诉用户。
/// </summary>
/// <param name="SessionId">会话编号。</param>
/// <param name="ProjectId">所属项目编号。</param>
/// <param name="EffectiveSeconds">这一轮的有效专注秒数。</param>
/// <param name="IdeaNoteCount">这一轮记下的想法条数（想法本身会保留，只是不再挂在这一轮上）。</param>
public sealed record SessionDeletionImpact(
    long SessionId,
    long ProjectId,
    long EffectiveSeconds,
    int IdeaNoteCount)
{
    public TimeSpan EffectiveTime => TimeSpan.FromSeconds(EffectiveSeconds);

    public string EffectiveText => TimeFormatter.ToChinese(EffectiveTime);
}
