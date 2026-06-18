using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;

namespace GourmetProject.Game.Gameplay
{
    /// <summary>
    /// 道具随机池与默认规则。当前 cfg.Item 还没有品质/标记/上限字段，因此这里集中提供保守默认值。
    /// </summary>
    public static class ItemPoolService
    {
        public static ItemAcquireResult GrantRandom(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int fallbackGold)
        {
            List<string> picked = Roll(tables, run, kind, rng, 1);
            if (picked.Count == 0)
            {
                run.Gold += fallbackGold;
                return new ItemAcquireResult(ItemAcquireOutcome.ConvertedToGold, null, null, 0, 0, fallbackGold);
            }

            return run.AcquireItem(picked[0], fallbackGold);
        }

        public static List<string> Roll(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int count)
        {
            var result = new List<string>();
            if (tables == null || run == null || rng == null || count <= 0)
            {
                return result;
            }

            List<cfg.Item> candidates = BuildCandidates(tables, run, kind);
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (cfg.Item item in candidates)
                {
                    weights.Add(GetWeight(run, item));
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index].Id);
                candidates.RemoveAt(index);
            }

            return result;
        }

        public static bool CanEnterPool(GameRun run, cfg.Item item)
        {
            if (run == null || item == null)
            {
                return false;
            }

            RunItemState state = run.GetItemState(item.Id);
            if (item.Kind == cfg.ItemKind.Passive)
            {
                return state == null || state.Level < GetPassiveMaxLevel(item);
            }

            int count = state?.Count ?? 0;
            int total = state?.TotalAcquired ?? 0;
            int holdLimit = GetActiveHoldLimit(item);
            int acquireLimit = GetActiveAcquireLimit(item);
            bool underHoldLimit = holdLimit <= 0 || count < holdLimit;
            bool underAcquireLimit = acquireLimit <= 0 || total < acquireLimit;
            return underHoldLimit && underAcquireLimit;
        }

        public static int GetPassiveMaxLevel(cfg.Item item)
        {
            if (item == null || item.Kind != cfg.ItemKind.Passive)
            {
                return 1;
            }

            return Math.Max(1, item.MaxLevel);
        }

        public static int GetActiveHoldLimit(cfg.Item item)
        {
            return item != null && item.Kind == cfg.ItemKind.Active ? item.HoldLimit : 1;
        }

        public static int GetActiveAcquireLimit(cfg.Item item)
        {
            return item != null && item.Kind == cfg.ItemKind.Active ? item.AcquireLimit : 1;
        }

        public static float GetScaledEffectValue(cfg.Item item, RunItemState state)
        {
            if (item == null)
            {
                return 0f;
            }

            int level = Math.Max(1, state?.Level ?? 1);
            return item.Kind == cfg.ItemKind.Passive ? item.EffectValue * level : item.EffectValue;
        }

        private static List<cfg.Item> BuildCandidates(cfg.Tables tables, GameRun run, cfg.ItemKind kind)
        {
            var candidates = new List<cfg.Item>();
            foreach (cfg.Item item in tables.TbItem.DataList)
            {
                if (item.Kind == kind && CanEnterPool(run, item))
                {
                    candidates.Add(item);
                }
            }

            return candidates;
        }

        private static float GetWeight(GameRun run, cfg.Item item)
        {
            float baseWeight = item.BaseWeight > 0f ? item.BaseWeight : 1f;

            RunItemState state = run.GetItemState(item.Id);
            if (item.Kind == cfg.ItemKind.Passive && state != null)
            {
                float multiplier = item.NextLevelWeightMultiplier > 0f ? item.NextLevelWeightMultiplier : 1f;
                return baseWeight * multiplier;
            }

            return baseWeight;
        }
    }
}
