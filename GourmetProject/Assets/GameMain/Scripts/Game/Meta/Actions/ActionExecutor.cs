using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Analytics;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动执行：推进天数、记录上下文，并按 <see cref="cfg.ActionBehavior"/> 分派到对应 handler 产出
    /// <see cref="ActionOutcome"/> 交给编排层。无论来源（随机 n 选一 / 时间轴放置节点）都走这一条路径。
    /// 真正的异步表现（经营挑战、商店、事件弹窗）由编排层（BattleForm）依据 outcome 处理。
    /// 注意：这里只做「解析」——设上下文、计算即时效果，均为内存态、不存档。
    /// 步数推进推迟到玩家明确结算时由 <see cref="Commit"/> 提交，避免进入即消耗行动。
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

            IActionBehaviorHandler handler = ActionBehaviorRegistry.Get(action.Behavior);
            return handler != null
                ? handler.Execute(run, context, rng)
                : ActionOutcome.Immediate(action.Desc);
        }

        /// <summary>
        /// 提交一次行动的进度：推进天数/步数。由编排层在玩家明确结算
        /// （商店退出、事件选完、经营挑战结算、通知点继续）时调用，存档由调用方负责。
        /// </summary>
        /// <returns>提交前的天数，用于结算刚跨过的时间轴节点。</returns>
        public static float Commit(GameRun run, ActionExecutionContext context)
        {
            if (run == null || context == null || context.Action == null)
            {
                return run?.CurrentDay ?? 0f;
            }

            float prevDay = run.CurrentDay;
            if (!context.IsDailyAction)
            {
                run.ClearPendingActionExecution();
                return prevDay;
            }

            float committedCostDays = ResolveTimelineStopCost(run, context);
            prevDay = TimelineService.AdvanceDays(run, committedCostDays);
            GameAnalyticsService.TrackCrossedDayCheckpoints(run, prevDay);
            if (context.HalfDayBuffApplied)
            {
                run.TryConsumeNextDailyActionHalfCostStack();
            }

            run.AdvanceActionStep();
            return prevDay;
        }

        private static float ResolveTimelineStopCost(GameRun run, ActionExecutionContext context)
        {
            float plannedCost = System.Math.Max(0f, context.CostDays);
            float chance = System.Math.Max(0f, System.Math.Min(1f, context.TimelineStopChance));
            if (plannedCost <= TimelineMath.Epsilon
                || chance <= 0f
                || GameApp.Random == null)
            {
                return plannedCost;
            }

            float projectedDay = TimelineMath.Advance(
                run.CurrentDay,
                plannedCost,
                run.TimelineLengthDays);
            var candidateDays = new SortedSet<int>();
            foreach (cfg.TimelineNode node in TimelineService.GetNodes(run))
            {
                if (node == null
                    || run.IsNodeTriggered(node.Id)
                    || node.Day < run.CurrentDay - TimelineMath.Epsilon
                    || node.Day > projectedDay + TimelineMath.Epsilon)
                {
                    continue;
                }

                candidateDays.Add(node.Day);
            }

            foreach (int day in candidateDays)
            {
                string key =
                    $"timeline_stop_r{context.RunStepIndex}_w{run.WeekIndex}_s{context.StepIndex}_d{day}";
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Item, key);
                if (rng == null || !rng.NextBool(chance))
                {
                    continue;
                }

                context.TimelineStopTriggered = true;
                context.TimelineStopDay = day;
                new ItemRuntime(run).FlashTriggered(m => m.TimelineStopChance() > 0f);
                return TimelineMath.Quantize(System.Math.Max(0f, day - run.CurrentDay));
            }

            return plannedCost;
        }
    }
}
