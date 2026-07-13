using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.Passives
{
    // 时间轴族：均需时间轴子系统，尚未实装。获得时类走占位日志。

    [Preserve]
    [PassiveItemModel("item_timeline_random")]
    public sealed class TimelineRandomizeModel : TodoOnAcquireModel
    {
        protected override string EffectName => "TimelineRandomize";
    }

    [Preserve]
    [PassiveItemModel("item_extra_day")]
    public sealed class TimelineExtraDayModel : TodoOnAcquireModel
    {
        protected override string EffectName => "TimelineExtraDay";
    }

    [Preserve]
    [PassiveItemModel("item_week_minus")]
    public sealed class TimelineWeekMinusModel : TodoOnAcquireModel
    {
        protected override string EffectName => "TimelineWeekMinus";
    }

    [Preserve]
    [PassiveItemModel("item_add_reward_node")]
    public sealed class TimelineAddRewardNodeModel : TodoOnAcquireModel
    {
        protected override string EffectName => "TimelineAddRewardNode";
    }

    [Preserve]
    [PassiveItemModel("item_add_interest_node")]
    public sealed class TimelineAddInterestNodeModel : TodoOnAcquireModel
    {
        protected override string EffectName => "TimelineAddInterestNode";
    }

    /// <summary>TODO(passive-item): 跳过节点，缺时间轴 hook；占位无副作用。</summary>
    [Preserve]
    [PassiveItemModel("item_skip_node")]
    public sealed class TimelineSkipNodeModel : PassiveItemModel
    {
    }
}
