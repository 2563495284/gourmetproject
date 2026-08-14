using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 餐桌材质效果：按「食物×材质」聚合执行，携带该食物占据本材质的格数 <c>cellCount</c>。
    /// 在 <see cref="ScorePhase.Materials"/> 阶段（技能/风味之后）触发，作用于当前结算的菜。
    /// </summary>
    public sealed class MaterialEffect : IScoreEffect
    {
        private readonly MaterialDef _def;
        private readonly int _cellCount;

        public MaterialEffect(MaterialDef def, int cellCount)
        {
            _def = def;
            _cellCount = cellCount < 1 ? 1 : cellCount;
        }

        public void Apply(ScoreContext ctx)
        {
            if (_def == null || ctx.Dish == null)
            {
                return;
            }

            float v = _def.EffectValue;
            switch (_def.MaterialEffect)
            {
                case MaterialEffectType.AddFlat:
                    ctx.AddFlat(v);
                    break;
                case MaterialEffectType.PermanentAddFlat:
                    ctx.AddPermanentFlatTo(ctx.Dish, v);
                    break;
                case MaterialEffectType.AddMultFlat:
                    ctx.AddMultFlatTo(ctx.Dish, v);
                    break;
                case MaterialEffectType.AddMult:
                    ctx.MultiplyBy(v);
                    break;
                case MaterialEffectType.AddMultFlatPerCell:
                    ctx.AddMultFlatTo(ctx.Dish, v * _cellCount);
                    break;
                case MaterialEffectType.GrantGoldIfCellCount:
                    if (_cellCount >= _def.ThresholdParam)
                    {
                        ctx.GrantGold(v);
                    }

                    break;
                case MaterialEffectType.GrantItemRollIfCellCount:
                    // 银材质按该食物实际占据的每个银格独立登记一次判定；
                    // 正式结算再对每次请求各掷 1/5，预览阶段仍不消耗随机流。
                    for (int i = 0; i < _cellCount; i++)
                    {
                        ctx.RequestSilverItemRoll(0.2f);
                    }

                    break;
            }
        }
    }
}
