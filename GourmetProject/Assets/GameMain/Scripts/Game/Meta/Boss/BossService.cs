using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 美食明细解析 + Boss 随机。Boss 已并入 <see cref="cfg.Food"/>（isBoss=true 的美食行）。
    /// Boss 随机按解锁条件筛选，从 Boss 候选中不放回按权重随机；候选抽光后重置抽取历史。
    /// </summary>
    public static class BossService
    {
        private const string Tag = "Boss";

        public static cfg.Food RollBoss(GameRun run, IRandomStream rng)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            var available = new List<cfg.Food>();
            foreach (cfg.Food boss in tables.TbFood.DataList)
            {
                if (boss.IsBoss && IsEligible(run, boss))
                {
                    available.Add(boss);
                }
            }

            if (available.Count == 0)
            {
                Log.Warning($"第 {run.WeekIndex} 周无可用 Boss。", Tag);
                return null;
            }

            List<cfg.Food> candidates = BuildUnrolledCandidates(run, available);
            if (candidates.Count == 0)
            {
                run.ResetBossRollHistory();
                candidates = new List<cfg.Food>(available);
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.Food boss in candidates)
            {
                weights.Add(boss.Weight > 0f ? boss.Weight : 1f);
            }

            return candidates[rng.WeightedPickIndex(weights)];
        }

        private static bool IsEligible(GameRun run, cfg.Food boss)
        {
            if (boss == null)
            {
                return false;
            }

            return PreconditionEvaluator.IsSatisfied(run, boss.UnlockCondition);
        }

        private static List<cfg.Food> BuildUnrolledCandidates(GameRun run, IReadOnlyList<cfg.Food> available)
        {
            var candidates = new List<cfg.Food>(available.Count);
            foreach (cfg.Food boss in available)
            {
                if (!run.IsBossRolled(boss.Id))
                {
                    candidates.Add(boss);
                }
            }

            return candidates;
        }
    }

    /// <summary>美食明细解析：把薄壳 Food 行动映射到其关联的 <see cref="cfg.Food"/> 明细。</summary>
    public static class FoodService
    {
        /// <summary>取 Food 行动关联的美食明细；非 Food 行为或未配 foodId 返回 null。</summary>
        public static cfg.Food Resolve(cfg.Tables tables, cfg.GameAction action)
        {
            if (action == null || action.Behavior != cfg.ActionBehavior.Food || string.IsNullOrEmpty(action.FoodId))
            {
                return null;
            }

            tables ??= GameApp.Config.Tables;
            return tables.TbFood.GetOrDefault(action.FoodId);
        }

        /// <summary>该 Food 行动是否为 Boss 槽（Food 行为且未绑定具体 foodId=执行时随机 Boss）。</summary>
        public static bool IsBossSlot(cfg.GameAction action)
        {
            return action != null && action.Behavior == cfg.ActionBehavior.Food && string.IsNullOrEmpty(action.FoodId);
        }
    }
}
