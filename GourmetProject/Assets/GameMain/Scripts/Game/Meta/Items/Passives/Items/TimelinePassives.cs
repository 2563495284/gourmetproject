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
        public override void OnAcquired()
        {
            Finish(PassiveTimelineMutationService.DelayBoss(Run, Def.Name, System.Math.Max(1, (int)Value)));
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
    }
}
