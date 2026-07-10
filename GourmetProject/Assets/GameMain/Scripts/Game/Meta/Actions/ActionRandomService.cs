using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动可用性：小组成员是否能进入本次 n 选一。Food 看是否解析到 <see cref="cfg.Food"/> 明细；
    /// Event/Reward/Negative 看对应事件池是否非空；Shop/Interest 恒可用。
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

                    return FoodService.Resolve(run.Tables, action) != null;
                }

                case cfg.ActionBehavior.Event:
                case cfg.ActionBehavior.Reward:
                case cfg.ActionBehavior.Negative:
                    return HasEligibleEvent(run, action.Behavior);

                default:
                    return true;
            }
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

                if (PreconditionEvaluator.IsSatisfied(run, ev.Preconditions))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
