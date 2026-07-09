using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动可用性：小组成员是否能进入本次 n 选一。薄壳行动的前置/可重复看其关联明细表
    /// （Food 看 <see cref="cfg.Food"/>，Event/Reward/Negative 看对应事件池是否非空），Shop/Interest 恒可用。
    /// </summary>
    public static class ActionRandomService
    {
        public const int MaxChoiceCount = 3;

        public static bool IsAvailable(GameRun run, cfg.GameAction action)
        {
            if (run == null || action == null)
            {
                return false;
            }

            switch (action.Behavior)
            {
                case cfg.ActionBehavior.Food:
                {
                    if (FoodService.IsBossSlot(action))
                    {
                        return true;
                    }

                    cfg.Food food = FoodService.Resolve(run.Tables, action);
                    if (food == null)
                    {
                        return false;
                    }

                    if (!food.Repeatable && run.IsActionUsed(action.Id))
                    {
                        return false;
                    }

                    return PreconditionEvaluator.IsSatisfied(run, food.Preconditions);
                }

                case cfg.ActionBehavior.Event:
                case cfg.ActionBehavior.Reward:
                case cfg.ActionBehavior.Negative:
                    return HasEligibleEvent(run, action.Behavior);

                default:
                    return true;
            }
        }

        /// <summary>行动结算「不可重复」判定：Food 看 TbFood.repeatable；其余交由事件层/不去重。</summary>
        public static bool IsRepeatable(GameRun run, cfg.GameAction action)
        {
            if (action == null)
            {
                return true;
            }

            if (action.Behavior == cfg.ActionBehavior.Food && !FoodService.IsBossSlot(action))
            {
                cfg.Food food = FoodService.Resolve(run?.Tables, action);
                return food == null || food.Repeatable;
            }

            return true;
        }

        private static bool HasEligibleEvent(GameRun run, cfg.ActionBehavior eventType)
        {
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if (ev.EventType != eventType || ev.Weight <= 0f)
                {
                    continue;
                }

                if ((ev.Repeatable || !run.IsEventUsed(ev.Id)) && PreconditionEvaluator.IsSatisfied(run, ev.Preconditions))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
