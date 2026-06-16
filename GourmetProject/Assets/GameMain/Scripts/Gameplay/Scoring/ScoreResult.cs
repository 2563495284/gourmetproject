using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>单个菜品的结算明细，供 UI 展示与单测断言。</summary>
    public sealed class DishScore
    {
        public DishScore(int dishInstanceId, string dishId, float baseValue, float flatBonus, float multiplier)
        {
            DishInstanceId = dishInstanceId;
            DishId = dishId;
            BaseValue = baseValue;
            FlatBonus = flatBonus;
            Multiplier = multiplier;
        }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public float BaseValue { get; }

        public float FlatBonus { get; }

        public float Multiplier { get; }

        /// <summary>本菜品最终贡献 = (美味度 + 加法) × 乘区。</summary>
        public float Contribution => (BaseValue + FlatBonus) * Multiplier;
    }

    /// <summary>一次结算的完整结果。</summary>
    public sealed class ScoreResult
    {
        public ScoreResult(
            IReadOnlyList<DishScore> dishScores,
            float rawSum,
            float finalFlat,
            float finalMultiplier,
            IReadOnlyList<ScoreLine> scoreLines = null,
            IReadOnlyList<ScoreEvent> scoreEvents = null)
        {
            DishScores = dishScores;
            RawSum = rawSum;
            FinalFlat = finalFlat;
            FinalMultiplier = finalMultiplier;
            ScoreLines = scoreLines ?? System.Array.Empty<ScoreLine>();
            ScoreEvents = scoreEvents ?? System.Array.Empty<ScoreEvent>();
        }

        /// <summary>逐菜结算明细（按结算顺序）。</summary>
        public IReadOnlyList<DishScore> DishScores { get; }

        /// <summary>所有菜品贡献之和（未计局级修正）。</summary>
        public float RawSum { get; }

        /// <summary>局级加法修正（道具/Buff 注入）。</summary>
        public float FinalFlat { get; }

        /// <summary>局级乘区修正（道具/Buff 注入）。</summary>
        public float FinalMultiplier { get; }

        /// <summary>可解释结算明细（按实际执行顺序）。</summary>
        public IReadOnlyList<ScoreLine> ScoreLines { get; }

        /// <summary>结算生命周期事件（按实际发生顺序）。</summary>
        public IReadOnlyList<ScoreEvent> ScoreEvents { get; }

        /// <summary>最终得分（四舍五入到整数，0.5 向上取整）。</summary>
        public int Total => (int)System.Math.Round((RawSum + FinalFlat) * FinalMultiplier, System.MidpointRounding.AwayFromZero);
    }

    /// <summary>结算生命周期事件。它记录“发生了什么”，不直接改变分数。</summary>
    public enum ScoreEventType
    {
        CalculationStarted = 0,
        CalculationFinished = 1,
        DishStarted = 2,
        DishCompleted = 3,
        EffectStarted = 4,
        EffectFinished = 5,
        CommandExecuted = 6,
    }

    /// <summary>一次结算事件记录，供调试、回放或 UI 演出使用。</summary>
    public sealed class ScoreEvent
    {
        public ScoreEvent(
            ScoreEventType type,
            ScorePhase phase,
            ScoreSource source,
            int dishInstanceId,
            string dishId,
            GridPos? cell,
            string message)
        {
            Type = type;
            Phase = phase;
            Source = source;
            DishInstanceId = dishInstanceId;
            DishId = dishId ?? string.Empty;
            Cell = cell;
            Message = message ?? string.Empty;
        }

        public ScoreEventType Type { get; }

        public ScorePhase Phase { get; }

        public ScoreSource Source { get; }

        public int DishInstanceId { get; }

        public string DishId { get; }

        public GridPos? Cell { get; }

        public string Message { get; }
    }
}
