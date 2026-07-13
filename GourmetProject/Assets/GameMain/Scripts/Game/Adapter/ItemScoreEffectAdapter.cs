using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Adapter
{
    /// <summary>
    /// 把当前 Run 持有的「结算类」被动道具适配为玩法层的 <see cref="IScoreEffectSource"/>。
    /// 局级加/乘（FinalAddFlat/Mult）走 BattleSession 快路径（模型 ApplyToBattle），不在此产出；
    /// 本适配器遍历持有道具模型的 <see cref="PassiveItemModel.BuildScoreSpecs"/> 收集逐菜/条件/顺序类规格。
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

            foreach (PassiveItemModel model in run.PassiveModels)
            {
                foreach (ItemScoreSpec spec in model.BuildScoreSpecs())
                {
                    if (spec.Type != ItemScoreEffectType.None)
                    {
                        specs.Add(spec);
                    }
                }
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

        /// <summary>本次结算每道菜的「视为食物数」额外加成（道具 CountAsBonusAll 汇总）。</summary>
        public static int ExtraCountAsPerDish(GameRun run)
        {
            if (run == null)
            {
                return 0;
            }

            int total = 0;
            foreach (PassiveItemModel model in run.PassiveModels)
            {
                total += model.ExtraCountAsPerDish();
            }

            return total;
        }
    }
}
