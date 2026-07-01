using System;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>已经确定目标和阶段的效果执行项。</summary>
    public sealed class ScoreEffectEntry
    {
        public ScoreEffectEntry(
            ScorePhase phase,
            ScoreSource source,
            IScoreEffect effect,
            DishInstance dish = null,
            IEffectDef tag = null,
            GridPos? cell = null,
            int priority = 0,
            int boardOrder = 0)
        {
            Phase = phase;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Dish = dish;
            Tag = tag;
            Cell = cell;
            Priority = priority;
            BoardOrder = boardOrder;
        }

        public ScorePhase Phase { get; }

        public ScoreSource Source { get; }

        public IScoreEffect Effect { get; }

        /// <summary>指定菜品时只在该菜品结算；为空且处于逐菜阶段时，会对每道菜执行一次。</summary>
        public DishInstance Dish { get; }

        public IEffectDef Tag { get; }

        public GridPos? Cell { get; }

        public int Priority { get; }

        public int BoardOrder { get; }

        public int Sequence { get; internal set; }
    }
}
