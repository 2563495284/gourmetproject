using System;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>美味度 +EffectValue。</summary>
    public sealed class AddFlatEffect : ITagEffect
    {
        public void Apply(EffectContext ctx) => ctx.AddFlat(ctx.Tag.EffectValue);
    }

    /// <summary>本菜品贡献 ×EffectValue。</summary>
    public sealed class AddMultEffect : ITagEffect
    {
        public void Apply(EffectContext ctx) => ctx.MultiplyBy(ctx.Tag.EffectValue);
    }

    /// <summary>每个相邻菜品，美味度 +EffectValue。</summary>
    public sealed class PerAdjacentDishEffect : ITagEffect
    {
        public void Apply(EffectContext ctx)
        {
            int adjacent = ctx.Board.GetAdjacentDishCount(ctx.Dish);
            ctx.AddFlat(ctx.Tag.EffectValue * adjacent);
        }
    }

    /// <summary>棋盘每个空位，美味度 +EffectValue。</summary>
    public sealed class PerEmptyCellEffect : ITagEffect
    {
        public void Apply(EffectContext ctx) => ctx.AddFlat(ctx.Tag.EffectValue * ctx.Board.EmptyCellCount);
    }

    /// <summary>本菜品每占用一格，美味度 +EffectValue。</summary>
    public sealed class PerOccupiedCellEffect : ITagEffect
    {
        public void Apply(EffectContext ctx) => ctx.AddFlat(ctx.Tag.EffectValue * ctx.Dish.OccupiedCells.Count);
    }

    /// <summary>棋盘每有一道菜，本菜品贡献 ×(1+EffectValue)（按菜数复利）。</summary>
    public sealed class PerDishOnBoardEffect : ITagEffect
    {
        public void Apply(EffectContext ctx)
        {
            int dishCount = ctx.Board.DishCount;
            float factor = (float)Math.Pow(1.0 + ctx.Tag.EffectValue, dishCount);
            ctx.MultiplyBy(factor);
        }
    }
}
