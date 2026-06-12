using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 运行时构建 uGUI 控件的轻量助手。局内/局外界面用代码搭建，避免手写复杂 prefab YAML：
    /// 每个界面 prefab 只需一个挂着界面逻辑脚本的根节点，子控件全部由代码生成。
    /// 统一使用归一化锚点，使布局随容器自适应。
    /// </summary>
    public static class UiBuilder
    {
        private static Font _font;

        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                {
                    // Unity 6 移除了内置 Arial，改用 LegacyRuntime.ttf。
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return _font;
            }
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>设置归一化锚点并清零偏移（可选四周像素内缩）。</summary>
        public static RectTransform Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float padding = 0f)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static Image AddImage(Transform parent, string name, Color color,
            float minX, float minY, float maxX, float maxY, float padding = 0f)
        {
            RectTransform rect = NewRect(name, parent);
            Anchor(rect, minX, minY, maxX, maxY, padding);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text AddText(Transform parent, string name, string content, int fontSize, Color color,
            float minX, float minY, float maxX, float maxY, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            RectTransform rect = NewRect(name, parent);
            Anchor(rect, minX, minY, maxX, maxY);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button AddButton(Transform parent, string name, string label, Color bgColor,
            float minX, float minY, float maxX, float maxY, UnityEngine.Events.UnityAction onClick = null, int fontSize = 26)
        {
            Image image = AddImage(parent, name, bgColor, minX, minY, maxX, maxY);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            if (!string.IsNullOrEmpty(label))
            {
                Text text = AddText(image.transform, "Label", label, fontSize, Color.white, 0f, 0f, 1f, 1f);
                text.raycastTarget = false;
            }

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        public static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }
        }

        /// <summary>由字符串稳定派生一个鲜明颜色，用于区分不同菜品。</summary>
        public static Color ColorFromString(string s)
        {
            int hash = 17;
            foreach (char c in s ?? string.Empty)
            {
                hash = unchecked(hash * 31 + c);
            }

            float hue = (Mathf.Abs(hash) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.9f);
        }
    }
}
