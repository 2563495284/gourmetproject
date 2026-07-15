using UnityEngine;

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
    }
}
