using System.Collections.Generic;

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
            float finalMultiplier)
        {
            DishScores = dishScores;
            RawSum = rawSum;
            FinalFlat = finalFlat;
            FinalMultiplier = finalMultiplier;
        }

        /// <summary>逐菜结算明细（按结算顺序）。</summary>
        public IReadOnlyList<DishScore> DishScores { get; }

        /// <summary>所有菜品贡献之和（未计局级修正）。</summary>
        public float RawSum { get; }

        /// <summary>局级加法修正（道具/Buff 注入）。</summary>
        public float FinalFlat { get; }

        /// <summary>局级乘区修正（道具/Buff 注入）。</summary>
        public float FinalMultiplier { get; }

        /// <summary>最终得分（四舍五入到整数，0.5 向上取整）。</summary>
        public int Total => (int)System.Math.Round((RawSum + FinalFlat) * FinalMultiplier, System.MidpointRounding.AwayFromZero);
    }
}
