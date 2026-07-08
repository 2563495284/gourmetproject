using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Adapter
{
    /// <summary>
    /// 把当前 Run 持有的「结算类」被动道具适配为玩法层的 <see cref="IScoreEffectSource"/>。
    /// 局级加/乘（FinalAddFlat/Mult）仍走 BattleSession 快路径，不在此产出；
    /// 本适配器只处理逐菜/条件/顺序类被动效果（TagBonus、永久加成、数量检测、上菜顺序等）。
    /// </summary>
    public static class ItemScoreEffectAdapter
    {
        /// <summary>从 Run 收集结算类被动道具规格。</summary>
        public static List<ItemScoreSpec> BuildSpecs(GameRun run)
        {
            var specs = new List<ItemScoreSpec>();
            if (run == null)
            {
                return specs;
            }

            foreach (RunItemState state in run.Items)
            {
                cfg.Item item = run.Tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Passive)
                {
                    continue;
                }

                ItemScoreEffectType type = MapEffectType(item.EffectType);
                if (type == ItemScoreEffectType.None)
                {
                    continue;
                }

                specs.Add(new ItemScoreSpec(type, item.EffectValue, item.EffectParam, state.Level, item.Id, item.Name));
            }

            return specs;
        }

        /// <summary>构建注入战斗结算的道具效果来源（空列表也返回空来源以保持稳定）。</summary>
        public static IReadOnlyList<IScoreEffectSource> BuildScoreSources(GameRun run)
        {
            List<ItemScoreSpec> specs = BuildSpecs(run);
            if (specs.Count == 0)
            {
                return System.Array.Empty<IScoreEffectSource>();
            }

            return new IScoreEffectSource[] { new ItemScoreEffectSource(specs) };
        }

        /// <summary>本次结算每道菜的「视为食物数」额外加成（道具 CountAsBonusAll 汇总；下限影响计数前提）。</summary>
        public static int ExtraCountAsPerDish(GameRun run)
        {
            if (run == null)
            {
                return 0;
            }

            int total = 0;
            foreach (RunItemState state in run.Items)
            {
                cfg.Item item = run.Tables.TbItem.GetOrDefault(state.ItemId);
                if (item == null || item.Kind != cfg.ItemKind.Passive)
                {
                    continue;
                }

                if (item.EffectType == ItemEffectTypes.CountAsBonusAll)
                {
                    total += (int)item.EffectValue * state.Level;
                }
            }

            return total;
        }

        private static ItemScoreEffectType MapEffectType(string effectType)
        {
            switch (effectType)
            {
                case ItemEffectTypes.TagBonus:
                    return ItemScoreEffectType.TagBonus;
                case ItemEffectTypes.PermanentAddFlatAll:
                    return ItemScoreEffectType.PermanentAddFlatAll;
                case ItemEffectTypes.PermanentAddMultAll:
                    return ItemScoreEffectType.PermanentAddMultAll;
                case ItemEffectTypes.CountThresholdFinalMult:
                    return ItemScoreEffectType.CountThresholdFinalMult;
                case ItemEffectTypes.PerDishSettledMultFlat:
                    return ItemScoreEffectType.PerDishSettledMultFlat;
                case ItemEffectTypes.PerSkillMultFlat:
                    return ItemScoreEffectType.PerSkillMultFlat;
                case ItemEffectTypes.NthServeMult:
                    return ItemScoreEffectType.NthServeMult;
                case ItemEffectTypes.EveryNthServeMult:
                    return ItemScoreEffectType.EveryNthServeMult;
                default:
                    return ItemScoreEffectType.None;
            }
        }
    }
}
