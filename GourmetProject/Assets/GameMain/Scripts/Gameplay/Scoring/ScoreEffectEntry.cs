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
            IEffectDef effectDef = null,
            GridPos? cell = null,
            int priority = 0,
            int boardOrder = 0,
            SkillExecutionTrace trace = null,
            SkillExecutionKind? skillKind = null)
        {
            Phase = phase;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            Dish = dish;
            EffectDef = effectDef;
            Cell = cell;
            Priority = priority;
            BoardOrder = boardOrder;
            Trace = trace;
            SkillKind = skillKind;
        }

        public ScorePhase Phase { get; }

        public ScoreSource Source { get; }

        public IScoreEffect Effect { get; }

        /// <summary>指定食物时只在该食物结算；为空且处于逐菜阶段时，会对每个食物执行一次。</summary>
        public DishInstance Dish { get; }

        public IEffectDef EffectDef { get; }

        public GridPos? Cell { get; }

        public int Priority { get; }

        public int BoardOrder { get; }

        public SkillExecutionTrace Trace { get; }

        /// <summary>
        /// 技能效果的稳定来源分类；非技能效果为空。该字段不依赖诊断 Trace，
        /// 因此关闭结算明细时仍可区分原生、复制与甜蜜传递技能。
        /// </summary>
        public SkillExecutionKind? SkillKind { get; }

        public int Sequence { get; internal set; }
    }
}
