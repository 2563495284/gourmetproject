using UnityEngine.Scripting;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta.Passives
{
    // 时间轴族：领取时修改当前周运行态时间轴，并交给 BattleForm 展示变化。

    public abstract class TimelineOnAcquireModel : PassiveItemModel
    {
        protected IRandomStream Rng()
        {
            string key = $"onacq_{ItemId}_w{Run.WeekIndex}_d{Run.CurrentDay:0.0}_s{Run.RunActionStepIndex}";
            return GameApp.Random?.DomainStream(SeedDomains.Item, key);
        }

        protected void Finish(TimelineMutationResult result)
        {
            MarkIconUsed();
            RunPersistence.Save(Run);
            PassiveMutationPresenter.ShowTimeline(Run, result);
        }
    }

    [Preserve]
    [PassiveItemModel("item_timeline_random")]
    public sealed class TimelineRandomizeModel : TimelineOnAcquireModel
    {
        public override void OnAcquired()
        {
            Finish(PassiveTimelineMutationService.Randomize(Run, Def.Name, Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_extra_day")]
    public sealed class TimelineExtraDayModel : TimelineOnAcquireModel
    {
        public override bool IsIconUsed => false;

        public override void ApplyToWeekTimeline()
        {
            Run?.EnsureTimelineLengthAtLeast(System.Math.Max(1, (int)Value));
        }

        public override void OnAcquired()
        {
            Finish(PassiveTimelineMutationService.EnsureLengthAtLeast(
                Run,
                Def.Name,
                System.Math.Max(1, (int)Value)));
        }
    }

    [Preserve]
    [PassiveItemModel("item_week_minus")]
    public sealed class TimelineWeekMinusModel : TimelineOnAcquireModel
    {
        public override void OnAcquired()
        {
            Finish(PassiveTimelineMutationService.DecreaseWeek(Run, Def.Name, System.Math.Max(1, (int)Value)));
        }
    }

    [Preserve]
    [PassiveItemModel("item_add_reward_node")]
    public sealed class TimelineAddRewardNodeModel : TimelineOnAcquireModel
    {
        public override void OnAcquired()
        {
            Finish(PassiveTimelineMutationService.AddNode(Run, Def.Name, "act_reward", Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_add_interest_node")]
    public sealed class TimelineAddInterestNodeModel : TimelineOnAcquireModel
    {
        public override void OnAcquired()
        {
            Finish(PassiveTimelineMutationService.AddNode(Run, Def.Name, "act_interest", Rng()));
        }
    }

    [Preserve]
    [PassiveItemModel("item_skip_node")]
    public sealed class TimelineSkipNodeModel : PassiveItemModel
    {
        public override bool SkipsTimelineBehavior(cfg.ActionBehavior behavior)
            => behavior == cfg.ActionBehavior.Interest;
    }

    [Preserve]
    [PassiveItemModel("item_skip_reward_node")]
    public sealed class TimelineSkipRewardNodeModel : PassiveItemModel
    {
        public override bool SkipsTimelineBehavior(cfg.ActionBehavior behavior)
            => behavior == cfg.ActionBehavior.Reward;
    }

    [Preserve]
    [PassiveItemModel("item_double_daily_cost_repeat_node")]
    public sealed class DoubleDailyCostRepeatNodeModel : PassiveItemModel
    {
        public override float DailyActionCostMultiplier() => Value > 0f ? Value : 1.5f;

        public override int TimelineNodeRepeatCount(cfg.ActionBehavior behavior)
            => behavior == cfg.ActionBehavior.Shop || behavior == cfg.ActionBehavior.Reward
                ? System.Math.Max(1, PassiveParam.ParseInt(Param, "repeat", 2))
                : 1;
    }

    [Preserve]
    [PassiveItemModel("item_timeline_stop_chance")]
    public sealed class TimelineStopChanceModel : PassiveItemModel
    {
        public override float TimelineStopChance() => Value;
    }
}
