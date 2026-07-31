using System;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>统一菜品美味值的实时计算与显示格式。</summary>
    public static class DishValueDisplay
    {
        public static float CurrentContribution(DishInstance dish)
        {
            return dish == null
                ? 0f
                : DishScore.CeilContribution(
                    dish.BaseScoreBeforeSettlement,
                    dish.BaseMultiplierBeforeSettlement);
        }

        public static string Format(float value)
        {
            float rounded = (float)Math.Round(
                Mathf.Max(0f, value),
                1,
                MidpointRounding.AwayFromZero);
            float whole = (float)Math.Round(rounded, MidpointRounding.AwayFromZero);
            return Mathf.Abs(rounded - whole) <= 0.001f
                ? $"{whole:0}"
                : $"{rounded:0.#}";
        }
    }
}
