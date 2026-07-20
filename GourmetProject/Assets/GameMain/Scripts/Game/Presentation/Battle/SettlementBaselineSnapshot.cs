using System.Collections.Generic;
using GourmetProject.Gameplay.Board;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>结算演出开始前的菜品分数基线，避免读取已写回永久效果后的实例值。</summary>
    public sealed class SettlementBaselineSnapshot
    {
        private readonly Dictionary<int, SettlementDishBaseline> _byDish = new();

        public void Capture(DishInstance dish)
        {
            if (dish == null)
            {
                return;
            }

            _byDish[dish.Id] = new SettlementDishBaseline(
                dish.BaseScoreBeforeSettlement,
                dish.BaseMultiplierBeforeSettlement);
        }

        public bool TryGet(int dishInstanceId, out SettlementDishBaseline baseline)
        {
            return _byDish.TryGetValue(dishInstanceId, out baseline);
        }
    }

    public readonly struct SettlementDishBaseline
    {
        public SettlementDishBaseline(float baseScore, float multiplier)
        {
            BaseScore = baseScore;
            Multiplier = multiplier;
        }

        public float BaseScore { get; }

        public float Multiplier { get; }
    }
}
