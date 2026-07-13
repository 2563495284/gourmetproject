using System;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>美味度 +EffectValue。</summary>
    public sealed class AddFlatEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx) => ctx.AddFlat(ctx.EffectDef.EffectValue);
    }

    /// <summary>本菜品贡献 ×EffectValue。</summary>
    public sealed class AddMultEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx) => ctx.MultiplyBy(ctx.EffectDef.EffectValue);
    }

    /// <summary>每个相邻菜品，美味度 +EffectValue。</summary>
    public sealed class PerAdjacentDishEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx)
        {
            int adjacent = ctx.DiningTable.GetAdjacentDishCount(ctx.Dish);
            ctx.AddFlat(ctx.EffectDef.EffectValue * adjacent);
        }
    }

    /// <summary>餐桌每个空位，美味度 +EffectValue。</summary>
    public sealed class PerEmptyCellEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx) => ctx.AddFlat(ctx.EffectDef.EffectValue * ctx.DiningTable.EmptyCellCount);
    }

    /// <summary>本菜品每占用一格，美味度 +EffectValue。</summary>
    public sealed class PerOccupiedCellEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx) => ctx.AddFlat(ctx.EffectDef.EffectValue * ctx.Dish.OccupiedCells.Count);
    }

    /// <summary>餐桌每有一道菜，本菜品贡献 ×(1+EffectValue)（按菜数复利）。</summary>
    public sealed class PerDishOnBoardEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx)
        {
            int dishCount = ctx.DiningTable.DishCount;
            float factor = (float)Math.Pow(1.0 + ctx.EffectDef.EffectValue, dishCount);
            ctx.MultiplyBy(factor);
        }
    }

    /// <summary>结算时获得 EffectValue 金币（锈）。金币是对外副作用，仅累积到 ScoreResult.GoldDelta。</summary>
    public sealed class GrantGoldEffect : IScoreEffect
    {
        public void Apply(ScoreContext ctx) => ctx.GrantGold(ctx.EffectDef.EffectValue);
    }

    /// <summary>最终总分 + 固定值。用于遗物、周规则或局级效果源。</summary>
    public sealed class AddFinalFlatEffect : IScoreEffect
    {
        private readonly float _value;

        public AddFinalFlatEffect(float value)
        {
            _value = value;
        }

        public void Apply(ScoreContext ctx) => ctx.AddFinalFlat(_value);
    }

    /// <summary>最终总分乘区 × 固定值。用于遗物、周规则或局级效果源。</summary>
    public sealed class MultiplyFinalEffect : IScoreEffect
    {
        private readonly float _value;

        public MultiplyFinalEffect(float value)
        {
            _value = value;
        }

        public void Apply(ScoreContext ctx) => ctx.MultiplyFinalBy(_value);
    }
}
