using GourmetProject.Core.Rng;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 行动执行：推进天数、记录「不可重复」行动，并按行动类型产出 <see cref="ActionOutcome"/> 交给编排层。
    /// 真正的异步表现（战斗、商店、事件弹窗）由编排层（BattleForm）依据 outcome 处理。
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
            int prevDay = TimelineService.AdvanceDays(run, context.CostDays);
            run.SetLastActionContext(context);
            run.AdvanceActionStep();
            if (!action.Repeatable)
            {
                run.MarkActionUsed(action.Id);
            }

            switch (action.ActionType)
            {
                case cfg.ActionType.Food:
                {
                    int required = HiddenScoreService.TargetScore(run, context);
                    string modifier = action.PayloadParam ?? string.Empty;
                    string key = $"food_w{run.WeekIndex}_s{context.StepIndex}_d{prevDay}_{action.Id}";
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
    }
}
