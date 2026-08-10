using BreakInfinity;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>统一食物美味值的实时计算与显示格式。</summary>
    public static class DishValueDisplay
    {
        public static BigDouble CurrentContribution(DishInstance dish)
        {
            return dish == null
                ? 0f
                : DishScore.CeilContribution(
                    dish.BaseScoreBeforeSettlement,
                    dish.BaseMultiplierBeforeSettlement);
        }

        public static string Format(BigDouble value)
        {
            return ScoreNumberFormatter.Format(BigDouble.Max(BigDouble.Zero, value));
        }
    }
}
