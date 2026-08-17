using System.Globalization;
using UnityEngine;
using BreakInfinity;
using GourmetProject.Gameplay.Scoring;

namespace GourmetProject.Game.UI.Tooltips
{
    internal static class FoodTipUiUtility
    {
        public static void ClearChildren(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Transform child = container.GetChild(i);
                if (Application.isPlaying)
                {
                    // Destroy is deferred until the end of the frame in play mode. Detach first so
                    // an immediate rebind cannot lay out or count stale cards alongside new ones.
                    child.gameObject.SetActive(false);
                    child.SetParent(null, false);
                    Object.Destroy(child.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        public static string FormatNumber(float value)
        {
            if (Mathf.Abs(value - Mathf.Round(value)) < 0.001f)
            {
                return Mathf.RoundToInt(value).ToString();
            }

            return value.ToString("0.##");
        }

        public static string FormatNumber(BigDouble value)
        {
            if (BigDouble.IsNaN(value) || BigDouble.IsInfinity(value))
            {
                return value.ToString();
            }

            if (BigDouble.Abs(value) >= ScoreNumberFormatter.ScientificThreshold)
            {
                return ScoreNumberFormatter.Format(value);
            }

            BigDouble rounded = BigDouble.Round(value, System.MidpointRounding.AwayFromZero);
            if (BigDouble.Abs(value - rounded) < 0.001d)
            {
                return rounded.ToString("F0");
            }

            return value.ToDouble().ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
