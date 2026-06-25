using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
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
            int fallbackGold,
            MetaProgressSaveData progress = null)
        {
            List<string> picked = Roll(tables, run, kind, rng, 1, progress);
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
            int count,
            MetaProgressSaveData progress = null)
        {
            int hidden = kind == cfg.ItemKind.Active
                ? HiddenScoreService.ActiveItemHiddenScore(run, run?.LastActionContext)
                : HiddenScoreService.PassiveItemHiddenScore(run, run?.LastActionContext);
            return Roll(tables, run, kind, rng, count, hidden, distanceFloor: 5, progress);
        }

        public static List<string> Roll(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            IRandomStream rng,
            int count,
            int hidden,
            int distanceFloor,
            MetaProgressSaveData progress = null)
        {
            var result = new List<string>();
            if (tables == null || run == null || rng == null || count <= 0)
            {
                return result;
            }

            bool activeItem = kind == cfg.ItemKind.Active;
            progress ??= MetaProgressPersistence.Load();
            List<cfg.Item> candidates = BuildCandidates(tables, run, kind, hidden, strictHidden: !activeItem, progress);
            if (candidates.Count == 0 && !activeItem)
            {
                candidates = BuildCandidates(tables, run, kind, hidden, strictHidden: false, progress);
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var weights = new List<float>(candidates.Count);
                foreach (cfg.Item item in candidates)
                {
                    weights.Add(GetWeight(run, item, hidden, distanceFloor));
                }

                int index = rng.WeightedPickIndex(weights);
                result.Add(candidates[index].Id);
                if (!activeItem)
                {
                    candidates.RemoveAt(index);
                }
            }

            return result;
        }

        public static bool CanEnterPool(GameRun run, cfg.Item item)
        {
            if (run == null || item == null)
            {
                return false;
            }

            if (item.Kind == cfg.ItemKind.Passive)
            {
                RunItemState state = run.GetItemState(item.Id);
                return state == null || state.Level < GetPassiveMaxLevel(item);
            }

            // 主动道具只看「持有上限」（当前持有几份实例）：达到上限则不再随机出，用掉一份腾出名额后又能被抽到。
            // 不再用「累计获得上限」，因为那会把已用掉的也算进去、与「只有存不存在」的模型冲突。
            int count = run.GetItemCount(item.Id);
            int holdLimit = GetActiveHoldLimit(item);
            return holdLimit <= 0 || count < holdLimit;
        }

        public static int GetPassiveMaxLevel(cfg.Item item)
        {
            if (item == null || item.Kind != cfg.ItemKind.Passive)
            {
                return 1;
            }

            return Math.Max(1, item.LevelWeightParams.MaxLevel);
        }

        public static int GetActiveHoldLimit(cfg.Item item)
        {
            return item != null && item.Kind == cfg.ItemKind.Active ? item.HoldLimit : 1;
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

        private static List<cfg.Item> BuildCandidates(
            cfg.Tables tables,
            GameRun run,
            cfg.ItemKind kind,
            int hidden,
            bool strictHidden,
            MetaProgressSaveData progress)
        {
            var candidates = new List<cfg.Item>();
            foreach (cfg.Item item in tables.TbItem.DataList)
            {
                if (item.Kind != kind || !CanEnterPool(run, item) || !MetaProgressService.IsItemUnlockedForPool(tables, item, progress))
                {
                    continue;
                }

                if (strictHidden && !CoversHidden(item, hidden))
                {
                    continue;
                }

                candidates.Add(item);
            }

            return candidates;
        }

        private static float GetWeight(GameRun run, cfg.Item item, int hidden, int distanceFloor)
        {
            float baseWeight = item.LevelWeightParams.BaseWeight > 0f ? item.LevelWeightParams.BaseWeight : 1f;
            baseWeight = RewardPoolService.HiddenScoreWeight(baseWeight, HiddenMean(item), hidden, distanceFloor);

            RunItemState state = run.GetItemState(item.Id);
            if (item.Kind == cfg.ItemKind.Passive && state != null)
            {
                float multiplier = item.LevelWeightParams.NextLevelWeightMultiplier > 0f ? item.LevelWeightParams.NextLevelWeightMultiplier : 1f;
                return baseWeight * multiplier;
            }

            return baseWeight;
        }

        private static bool CoversHidden(cfg.Item item, int hidden)
        {
            if (item.HiddenRange.Min == 0 && item.HiddenRange.Max == 0)
            {
                return true;
            }

            return hidden >= item.HiddenRange.Min && hidden <= item.HiddenRange.Max;
        }

        private static float HiddenMean(cfg.Item item)
        {
            if (item.HiddenRange.Min == 0 && item.HiddenRange.Max == 0)
            {
                return 0f;
            }

            return (item.HiddenRange.Min + item.HiddenRange.Max) * 0.5f;
        }
    }
}
