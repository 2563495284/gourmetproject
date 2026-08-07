using GourmetProject.Runtime;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动可用性：候选行动是否能进入本次随机池。Food 看是否解析到 <see cref="cfg.Food"/> 明细；
    /// Event/Reward/Negative 看对应事件池是否非空；Shop/Interest 恒可用。
    /// </summary>
    public static class ActionRandomService
    {
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
                    if (FoodService.IsBossAction(run.Tables, action))
                    {
                        return true;
                    }

                    return FoodService.Resolve(run.Tables, action) != null;
                }

                // act_event 从「全类型合并池」抽取，故只要任意类型事件可用即可展示。
                case cfg.ActionBehavior.Event:
                    return HasEligibleEvent(run, null);

                case cfg.ActionBehavior.Reward:
                case cfg.ActionBehavior.Negative:
                    return HasEligibleEvent(run, action.Behavior);

                case cfg.ActionBehavior.Slot:
                    return SlotService.TryGetConfig(run, action, out _, out _);

                default:
                    return true;
            }
        }

        /// <summary><paramref name="eventType"/> 为 null 时使用 Event/Reward/Negative 合并池。</summary>
        private static bool HasEligibleEvent(GameRun run, cfg.ActionBehavior? eventType)
        {
            cfg.Tables tables = run.Tables ?? GameApp.Config.Tables;
            foreach (cfg.GameEvent ev in tables.TbEvent.DataList)
            {
                if (ev.Weight <= 0f
                    || (eventType.HasValue && !ev.HasEventType(eventType.Value))
                    || (!eventType.HasValue && !ev.IsActionEventPoolMember))
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
