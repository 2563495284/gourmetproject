using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta.BossDebuffs;
using GourmetProject.Runtime;
using Log = GourmetProject.Core.Diagnostics.Log;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>
    /// 美食明细解析 + Boss 解析。Boss 固定为 <see cref="cfg.Food"/> 中唯一 actionKind=Feast 的行；
    /// 局内变化由 <see cref="RollBossDebuff"/> 不放回随机承担。
    /// </summary>
    public static class BossService
    {
        private const string Tag = "Boss";

        /// <summary>返回 TbFood 中唯一的 Boss 行；未配置时返回 null。</summary>
        public static cfg.Food ResolveBossFood(GameRun run)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            foreach (cfg.Food food in tables.TbFood.DataList)
            {
                if (FoodService.IsFeastFood(food))
                {
                    return food;
                }
            }

            Log.Warning($"第 {run?.WeekIndex ?? 0} 周未配置 Boss 美食（actionKind=Feast）。", Tag);
            return null;
        }

        public static cfg.BossDebuff RollBossDebuff(GameRun run, IRandomStream rng, bool mutateHistoryOnExhaustion = true)
        {
            cfg.Tables tables = run?.Tables ?? GameApp.Config.Tables;
            BossDebuffModelRegistry.ValidateDefinitions(tables);
            var available = new List<cfg.BossDebuff>();
            foreach (cfg.BossDebuff debuff in tables.TbBossDebuff.DataList)
            {
                if (IsEligible(run, debuff))
                {
                    available.Add(debuff);
                }
            }

            if (available.Count == 0)
            {
                Log.Warning($"第 {run?.WeekIndex ?? 0} 周无可用 Boss Debuff。", Tag);
                return null;
            }

            cfg.BossDebuff forced = ResolveForcedDebuff(run, available);
            if (forced != null)
            {
                return forced;
            }

            List<cfg.BossDebuff> candidates = BuildUnrolledDebuffCandidates(run, available);
            if (candidates.Count == 0)
            {
                if (mutateHistoryOnExhaustion)
                {
                    run.ResetBossDebuffRollHistory();
                }

                candidates = new List<cfg.BossDebuff>(available);
            }

            var weights = new List<float>(candidates.Count);
            foreach (cfg.BossDebuff debuff in candidates)
            {
                float defaultWeight = System.Math.Max(float.Epsilon, tables.TbGameBase.DefaultRandomWeight);
                weights.Add(debuff.Weight > 0f ? debuff.Weight : defaultWeight);
            }

            return candidates[rng.WeightedPickIndex(weights)];
        }

        public static string BuildBossDebuffSeedKey(GameRun run, string bossKey, string sourceNodeId = null)
        {
            int rerollIndex = run != null
                && !string.IsNullOrEmpty(sourceNodeId)
                && sourceNodeId == run.BossDebuffRerollNodeId
                    ? run.BossDebuffRerollIndex
                    : 0;
            return rerollIndex > 0
                ? $"{bossKey}_debuff_r{rerollIndex}"
                : $"{bossKey}_debuff";
        }

        private static cfg.BossDebuff ResolveForcedDebuff(GameRun run, IReadOnlyList<cfg.BossDebuff> available)
        {
            string forcedId = run?.ForcedBossDebuffId;
            if (string.IsNullOrEmpty(forcedId))
            {
                return null;
            }

            foreach (cfg.BossDebuff debuff in available)
            {
                if (debuff != null && debuff.Id == forcedId)
                {
                    return debuff;
                }
            }

            return null;
        }

        private static bool IsEligible(GameRun run, cfg.BossDebuff debuff)
        {
            if (debuff == null)
            {
                return false;
            }

            return PreconditionEvaluator.IsSatisfied(run, debuff.UnlockCondition);
        }

        private static List<cfg.BossDebuff> BuildUnrolledDebuffCandidates(GameRun run, IReadOnlyList<cfg.BossDebuff> available)
        {
            var candidates = new List<cfg.BossDebuff>(available.Count);
            foreach (cfg.BossDebuff debuff in available)
            {
                if (!run.IsBossDebuffRolled(debuff.Id))
                {
                    candidates.Add(debuff);
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

            tables ??= GameApp.Config?.Tables;
            if (tables == null)
            {
                return null;
            }

            return tables.TbFood.GetOrDefault(action.FoodId);
        }

        public static cfg.Food ResolveBoss(GameRun run, cfg.GameAction action)
        {
            if (action == null || action.Behavior != cfg.ActionBehavior.Food)
            {
                return null;
            }

            if (string.IsNullOrEmpty(action.FoodId))
            {
                return BossService.ResolveBossFood(run);
            }

            cfg.Food food = Resolve(run?.Tables, action);
            return IsFeastFood(food) ? food : null;
        }

        public static bool IsBossAction(cfg.Tables tables, cfg.GameAction action)
        {
            if (action == null || action.Behavior != cfg.ActionBehavior.Food)
            {
                return false;
            }

            if (string.IsNullOrEmpty(action.FoodId))
            {
                return true;
            }

            cfg.Food food = Resolve(tables, action);
            return IsFeastFood(food);
        }

        /// <summary>该 Food 行动是否为 Boss 槽（Food 行为且未绑定具体 foodId=执行时取唯一 Boss 美食）。</summary>
        public static bool IsBossSlot(cfg.GameAction action)
        {
            return action != null && action.Behavior == cfg.ActionBehavior.Food && string.IsNullOrEmpty(action.FoodId);
        }

        public static bool IsFeastFood(cfg.Food food)
        {
            return food != null && food.ActionKind == cfg.FoodActionKind.Feast;
        }
    }
}
