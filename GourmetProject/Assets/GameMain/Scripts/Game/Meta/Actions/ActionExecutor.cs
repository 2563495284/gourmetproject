using System.Globalization;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动执行：推进天数、记录上下文，并按行动类型产出 <see cref="ActionOutcome"/> 交给编排层。
    /// 真正的异步表现（战斗、商店、事件弹窗）由编排层（BattleForm）依据 outcome 处理。
    /// 注意：这里只做「解析」——设上下文、计算即时效果，均为内存态、不存档。
    /// 步数推进与「不可重复」标记推迟到玩家明确结算时由 <see cref="Commit"/> 提交，避免进入即消耗行动。
    /// </summary>
    public static class ActionExecutor
    {
        public static ActionOutcome Execute(GameRun run, cfg.GameAction action, IRandomStream rng)
        {
            return Execute(run, new ActionExecutionContext(action), rng);
        }

        public static ActionOutcome Execute(GameRun run, ActionExecutionContext context, IRandomStream rng)
        {
            if (run == null || context == null || context.Action == null)
            {
                return ActionOutcome.Immediate(string.Empty);
            }

            cfg.GameAction action = context.Action;
            run.SetLastActionContext(context);

            switch (action.ActionType)
            {
                case cfg.ActionType.Food:
                {
                    int required = HiddenScoreService.TargetScore(run, context);
                    string modifier = action.PayloadParam ?? string.Empty;
                    string key = $"food_w{run.WeekIndex}_s{context.StepIndex}_d{run.CurrentDay.ToString("0.0", CultureInfo.InvariantCulture)}_{action.Id}";
                    return ActionOutcome.Battle(required, modifier, key);
                }

                case cfg.ActionType.Event:
                    return ActionOutcome.Event(action.LinkId);

                case cfg.ActionType.Shop:
                    return ActionOutcome.Shop();

                case cfg.ActionType.Reward:
                case cfg.ActionType.Negative:
                {
                    string feedback = EffectResolver.Apply(run, action.PayloadType, action.PayloadValue, action.PayloadParam, rng);
                    return ActionOutcome.Immediate(string.IsNullOrEmpty(feedback) ? action.Desc : feedback);
                }

                default:
                    return ActionOutcome.Immediate(action.Desc);
            }
        }

        /// <summary>
        /// 提交一次行动的进度：推进天数/步数、标记「不可重复」行动。由编排层在玩家明确结算
        /// （商店退出、事件选完、战斗结算、通知点继续）时调用，存档由调用方负责。
        /// </summary>
        /// <returns>提交前的天数，用于结算刚跨过的行动轴节点。</returns>
        public static float Commit(GameRun run, ActionExecutionContext context)
        {
            if (run == null || context == null || context.Action == null)
            {
                return run?.CurrentDay ?? 0f;
            }

            float prevDay = TimelineService.AdvanceDays(run, context.CostDays);
            run.AdvanceActionStep();
            if (!context.Action.Repeatable)
            {
                run.MarkActionUsed(context.Action.Id);
            }

            return prevDay;
        }
    }
}
