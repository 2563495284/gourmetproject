using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 行动随机：从满足前置条件、未被「不可重复」排除的行动中，按权重做不放回随机，产出三选一。
    /// </summary>
    public static class ActionRandomService
    {
        public const int ChoiceCount = 3;

        public static List<cfg.GameAction> GenerateChoices(GameRun run, IRandomStream rng, int count = ChoiceCount)
        {
            var result = new List<cfg.GameAction>();
            if (run == null || rng == null || count <= 0)
            {
                return result;
            }

            var candidates = new List<cfg.GameAction>();
            foreach (cfg.GameAction action in GameApp.Config.Tables.TbAction.DataList)
            {
                if (IsAvailable(run, action))
                {
                    candidates.Add(action);
                }
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (cfg.GameAction action in candidates)
                {
                    weights.Add(action.Weight > 0f ? action.Weight : 1f);
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            return result;
        }

        /// <summary>行动是否可进入随机池：可重复或本周未用过，且满足前置条件。</summary>
        public static bool IsAvailable(GameRun run, cfg.GameAction action)
        {
            if (action == null)
            {
                return false;
            }

            if (!action.Repeatable && run.IsActionUsed(action.Id))
            {
                return false;
            }

            return PreconditionEvaluator.IsSatisfied(run, action.Preconditions);
        }
    }
}
