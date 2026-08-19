using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.Run
{
    /// <summary>
    /// BattleSession 结算结果写回 GameRun 的唯一入口。表现层可以分两阶段调用；
    /// 无界面模拟可直接调用 ApplyAll。每个阶段在 session 上保证幂等。
    /// </summary>
    public static class BattleSettlementApplier
    {
        public static BattleRunSettlement ApplyAll(GameRun run, BattleSession session)
        {
            ApplyRecipeGrowth(run, session);
            return ApplyFinal(run, session);
        }

        public static bool ApplyRecipeGrowth(GameRun run, BattleSession session)
        {
            if (run == null || session == null || !session.IsSettled
                || !session.TryMarkRunRecipeGrowthApplied())
            {
                return false;
            }

            foreach (RecipeScoreFlatDelta delta in session.LastRecipeScoreFlatDeltas)
            {
                run.AddRecipeScoreFlat(delta.DishIndex, delta.Delta);
            }

            foreach (RecipeScoreMultiplierDelta delta in session.LastRecipeScoreMultiplierDeltas)
            {
                run.MultiplyRecipeScore(delta.DishIndex, delta.Multiplier);
            }

            return true;
        }

        public static BattleRunSettlement ApplyFinal(GameRun run, BattleSession session)
        {
            var result = new BattleRunSettlement();
            if (run == null || session == null || !session.IsSettled
                || !session.TryMarkRunSettlementApplied())
            {
                return result;
            }

            var removedIndices = new HashSet<int>();
            foreach (RecipeRemovalOutcome outcome in session.LastRecipeRemovalOutcomes)
            {
                if (outcome.Removed && outcome.Request.SourceDishIndex >= 0)
                {
                    removedIndices.Add(outcome.Request.SourceDishIndex);
                }
            }

            foreach (int dishIndex in removedIndices.OrderByDescending(index => index))
            {
                if (run.RemoveBonusDishAt(dishIndex))
                {
                    result.RemovedRecipeIndices.Add(dishIndex);
                }
            }

            int goldBeforeSettlement = run.Gold;
            int gold = (int)Math.Round(session.PendingGold, MidpointRounding.AwayFromZero);
            if (gold != 0)
            {
                run.Gold = Math.Max(0, run.Gold + gold);
            }

            run.AddSettledCounts(session.LastSettledIncrements);
            result.GoldDelta = run.Gold - goldBeforeSettlement;
            result.Applied = true;
            return result;
        }
    }

    public sealed class BattleRunSettlement
    {
        public bool Applied;
        public int GoldDelta;
        public List<int> RemovedRecipeIndices { get; } = new List<int>();
    }
}
