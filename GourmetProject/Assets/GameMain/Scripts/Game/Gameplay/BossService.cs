using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// Boss 随机：按角色池、周限定、解锁条件筛选，再按权重随机一个 Boss。
    /// <paramref name="poolFilter"/>（来自节点 payloadParam）非空时进一步限定 Boss id（逗号分隔）。
    /// </summary>
    public static class BossService
    {
        private const string Tag = "Boss";

        public static cfg.Boss RollBoss(GameRun run, IRandomStream rng, string poolFilter = "")
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            cfg.Character character = tables.TbCharacter.GetOrDefault(run.CharacterId);
            var candidates = new List<cfg.Boss>();
            foreach (cfg.Boss boss in tables.TbBoss.DataList)
            {
                if (IsEligible(run, boss, character?.BossPool, poolFilter))
                {
                    candidates.Add(boss);
                }
            }

            if (candidates.Count == 0)
            {
                Log.Warning($"第 {run.WeekIndex} 周无可用 Boss（character={run.CharacterId}, filter='{poolFilter}'）。", Tag);
                return null;
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.Boss boss in candidates)
            {
                weights.Add(boss.Weight > 0f ? boss.Weight : 1f);
            }

            return candidates[rng.WeightedPickIndex(weights)];
        }

        private static bool IsEligible(GameRun run, cfg.Boss boss, string characterBossPool, string nodeBossPool)
        {
            if (boss == null)
            {
                return false;
            }

            bool isFinalWeek = !run.IsEndless && run.WeekIndex >= run.TotalWeeks;
            if (isFinalWeek && boss.Week != run.TotalWeeks)
            {
                return false;
            }

            if (boss.Week != 0 && boss.Week != run.WeekIndex)
            {
                return false;
            }

            if (boss.Week == 0 && run.IsBossCompleted(boss.Id))
            {
                return false;
            }

            if (!MatchesPool(boss.CharacterPool, run.CharacterId))
            {
                return false;
            }

            if (!MatchesPool(characterBossPool, boss.Id))
            {
                return false;
            }

            if (!MatchesPool(nodeBossPool, boss.Id))
            {
                return false;
            }

            return PreconditionEvaluator.IsSatisfied(run, boss.UnlockCondition);
        }

        /// <summary>逗号分隔池匹配：空池=任意通过；否则需包含 value。</summary>
        private static bool MatchesPool(string pool, string value)
        {
            if (string.IsNullOrEmpty(pool))
            {
                return true;
            }

            foreach (string part in pool.Split(','))
            {
                if (part.Trim() == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
