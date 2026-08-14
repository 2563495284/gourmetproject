using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Adapter
{
    /// <summary>
    /// 把当前 Run 持有的「结算类」装饰品适配为玩法层的 <see cref="IScoreEffectSource"/>。
    /// FinalAddFlat/Mult 仅保留兼容枚举，当前没有配置或模型产出；本适配器遍历持有装饰品和消耗品模型的
    /// <see cref="PassiveItemModel.BuildScoreSpecs"/> 收集现有逐菜/条件/顺序类规格。
    /// </summary>
    public static class ItemScoreEffectAdapter
    {
        /// <summary>从 Run 收集结算类装饰品规格。</summary>
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

        /// <summary>
        /// 构建注入经营挑战结算的装饰品和消耗品效果来源。来源在每次计分时才从 Run 收集规格，
        /// 使红心数等会在经营挑战页期间变化的条件以结算当刻为准。
        /// </summary>
        public static IReadOnlyList<IScoreEffectSource> BuildScoreSources(GameRun run)
        {
            if (run == null)
            {
                return System.Array.Empty<IScoreEffectSource>();
            }

            return new IScoreEffectSource[] { new RuntimePassiveItemScoreEffectSource(run) };
        }

        /// <summary>
        /// 经营挑战会话可以比结算早很多时间创建，因此不在构建会话时固化装饰品和消耗品列表和动态数值。
        /// </summary>
        private sealed class RuntimePassiveItemScoreEffectSource : IScoreEffectSource
        {
            private readonly GameRun _run;

            public RuntimePassiveItemScoreEffectSource(GameRun run)
            {
                _run = run;
            }

            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                List<ItemScoreSpec> specs = BuildSpecs(_run);
                if (specs.Count > 0)
                {
                    new ItemScoreEffectSource(specs).CollectEffects(snapshot, collector);
                }
            }
        }

        /// <summary>本次结算每个食物的「视为食物数」额外加成（装饰品和消耗品 CountAsBonusAll 汇总）。</summary>
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
