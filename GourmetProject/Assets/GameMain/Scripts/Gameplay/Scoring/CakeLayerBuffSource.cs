using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 欢乐蛋糕层数分段 buff 来源：在 <see cref="ScorePhase.AfterAllDishes"/>（所有食物/风味/标签之后、
    /// 汇总之前）产出一条全局效果，读全局层数并把满足阈值的每一档累计应用到对应分类的所有食物。
    /// 表 <c>TbCakeLayerBuff</c> 为空时不产出任何条目（默认无副作用）。
    /// </summary>
    public sealed class CakeLayerBuffSource : IScoreEffectSource
    {
        public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
        {
            IReadOnlyList<CakeLayerBuffDef> buffs = snapshot.Db?.CakeLayerBuffs;
            if (buffs == null || buffs.Count == 0)
            {
                return;
            }

            collector.Add(new ScoreEffectEntry(
                ScorePhase.AfterAllDishes,
                ScoreSource.TableTag("cake_layer_buff", "欢乐蛋糕层数"),
                new CakeLayerBuffEffect(buffs),
                dish: null));
        }
    }

    /// <summary>层数分段 buff 的应用：按阈值升序，layers &gt;= 阈值的每档累计作用到该档分类的所有食物。</summary>
    public sealed class CakeLayerBuffEffect : IScoreEffect
    {
        private readonly IReadOnlyList<CakeLayerBuffDef> _buffs;

        public CakeLayerBuffEffect(IReadOnlyList<CakeLayerBuffDef> buffs)
        {
            _buffs = buffs;
        }

        public void Apply(ScoreContext ctx)
        {
            int layers = ctx.CurrentHappyCakeLayers;
            if (layers <= 0 || _buffs == null)
            {
                return;
            }

            // 装饰品和消耗品「蛋糕捷径」下调每档需求层数（下限 0）。
            int reduction = ctx.Snapshot?.CakeLayerThresholdReduction ?? 0;

            foreach (CakeLayerBuffDef buff in _buffs)
            {
                int threshold = System.Math.Max(0, buff.Threshold - reduction);
                if (layers < threshold)
                {
                    continue;
                }

                float value = buff.ValuePerLayer * layers;
                List<DishInstance> targets = ctx.DiningTable.Dishes
                    .Where(dish => ctx.IsCategory(dish, buff.Category))
                    .ToList();
                foreach (DishInstance target in targets)
                {
                    switch (buff.EffectType)
                    {
                        case SkillActionType.AddFlat:
                            ctx.AddFlatTo(target, value);
                            break;

                        case SkillActionType.AddMultFlat:
                            ctx.AddMultFlatTo(target, value);
                            break;

                        case SkillActionType.AddMult:
                            ctx.MultiplyTo(target, value);
                            break;
                    }
                }
            }
        }
    }
}
