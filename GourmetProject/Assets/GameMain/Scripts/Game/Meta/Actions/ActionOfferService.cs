using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Game.Tutorial;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 行动候选的正式领域入口。负责 pending 恢复、命名随机流和局内修正，
    /// 让有界自动玩家与 BattleForm 使用完全相同的候选。
    /// </summary>
    public static class ActionOfferService
    {
        public static List<ActionChoice> GetOrRoll(GameRun run)
        {
            if (run == null)
            {
                return new List<ActionChoice>();
            }

            string key = GameRun.BuildActionChoiceKey(
                run.RunActionStepIndex,
                run.WeekIndex,
                run.CurrentDay,
                run.ActionStepIndex);
            if (run.HasPendingActionChoices(key))
            {
                List<ActionChoice> pending = run.GetPendingActionChoices(key);
                if (TutorialActionScheduleOverride.TryRefreshLegacyPendingChoices(
                        run,
                        pending,
                        out List<ActionChoice> refreshed))
                {
                    run.ClearPendingActionChoices();
                    run.SetPendingActionChoices(key, refreshed);
                    run.RequestSave();
                    pending = refreshed;
                }

                return ApplyRuntimeModifiers(run, pending);
            }

            List<ActionChoice> choices;
            if (!TutorialActionScheduleOverride.TryBuildChoices(run, out choices))
            {
                IRandomStream rng = run.Random.DomainStream(SeedDomains.Action, key);
                choices = ActionScheduleService.GenerateChoices(run, rng);
            }

            run.SetPendingActionChoices(key, choices);
            run.RequestSave();
            return ApplyRuntimeModifiers(run, choices);
        }

        public static List<ActionChoice> Reroll(GameRun run)
        {
            if (run == null || !run.TrySpendActionReroll())
            {
                return GetOrRoll(run);
            }

            return RerollAfterSpend(run);
        }

        public static List<ActionChoice> RerollAfterSpend(GameRun run)
        {
            if (run == null)
            {
                return new List<ActionChoice>();
            }

            string key = GameRun.BuildActionChoiceKey(
                run.RunActionStepIndex,
                run.WeekIndex,
                run.CurrentDay,
                run.ActionStepIndex);
            IRandomStream rng = run.Random.DomainStream(
                SeedDomains.Action,
                key + "_player_reroll_" + run.NextActiveUseKey());
            List<ActionChoice> choices = ActionScheduleService.RerollChoices(run, rng);
            run.SetPendingActionChoices(key, choices);
            run.RequestSave();
            return ApplyRuntimeModifiers(run, choices);
        }

        public static void Resolve(GameRun run)
        {
            run?.ClearPendingActionChoices();
        }

        private static List<ActionChoice> ApplyRuntimeModifiers(
            GameRun run,
            IReadOnlyList<ActionChoice> baseChoices)
        {
            var result = new List<ActionChoice>();
            if (baseChoices == null)
            {
                return result;
            }

            bool applyHalfDay = run != null && run.NextDailyActionHalfCostStacks > 0;
            foreach (ActionChoice choice in baseChoices)
            {
                if (choice == null)
                {
                    continue;
                }

                result.Add(new ActionChoice(
                    choice.Action,
                    choice.ActionGroupId,
                    choice.WeekStepIndex,
                    choice.RunStepIndex,
                    applyHalfDay ? run.PreviewDailyActionCost(choice.CostDays) : choice.CostDays,
                    halfDayBuffApplied: applyHalfDay,
                    timelineStopChance: choice.TimelineStopChance,
                    costBeforeHalfDays: choice.CostDays));
            }

            return result;
        }
    }
}
