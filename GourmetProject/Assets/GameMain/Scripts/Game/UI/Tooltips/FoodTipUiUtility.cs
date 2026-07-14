using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    internal static class FoodTipUiUtility
    {
        private static Font s_font;

        public static Font DefaultFont
        {
            get
            {
                if (s_font == null)
                {
                    s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                return s_font;
            }
        }

        public static RectTransform EnsureRect(GameObject go)
        {
            RectTransform rect = go.transform as RectTransform;
            if (rect != null)
            {
                return rect;
            }

            return go.AddComponent<RectTransform>();
        }

        public static Image EnsurePanelImage(GameObject go, Color color)
        {
            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = go.AddComponent<Image>();
            }

            image.color = color;
            image.raycastTarget = false;

            Outline outline = go.GetComponent<Outline>();
            if (outline == null)
            {
                outline = go.AddComponent<Outline>();
            }

            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, -2f);
            return image;
        }

        public static Text EnsureTextChild(
            RectTransform parent,
            Text current,
            string name,
            int fontSize,
            FontStyle style,
            TextAnchor anchor)
        {
            if (current != null)
            {
                return current;
            }

            Transform found = parent.Find(name);
            GameObject go = found != null ? found.gameObject : new GameObject(name, typeof(RectTransform));
            RectTransform rect = EnsureRect(go);
            if (found == null)
            {
                rect.SetParent(parent, false);
            }

            Text text = go.GetComponent<Text>();
            if (text == null)
            {
                text = go.AddComponent<Text>();
            }

            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.black;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.minHeight = fontSize + 8f;
            layout.preferredHeight = -1f;
            return text;
        }

        public static RectTransform CreateChild(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

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
