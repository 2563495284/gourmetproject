using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 把带真实透明圆角的手绘奶油鼠尾草底图接入出餐口与临时桌。
    /// 出餐口只在内区叠一层很淡的状态色，避免运行状态把手绘边框整体染色。
    /// </summary>
    internal static class CreamSageHudPanelStyle
    {
        internal const int TemporaryTableContentSiblingIndex = 0;

        private const string ServingSpritePath = "Sprites/UI/serving_outlet_cream_sage";
        private const string TemporarySpritePath = "Sprites/UI/temporary_table_cream_sage";
        private const string ServingStateLayerName = "CreamSageServingState";

        // 前两版运行时生成的旧层名。脚本热重载时隐藏，避免残留细线或遮罩。
        private static readonly string[] LegacyLayerNames =
        {
            "CreamSageServingCream",
            "CreamSageServingFill",
            "CreamSageTemporaryCream",
            "CreamSageTemporaryFill",
            "CreamSageTemporaryLedge",
            "CreamSageServingMask",
            "CreamSageTemporaryMask",
        };

        internal static Image ApplyServingOutlet(Image root)
        {
            if (root == null) return null;

            ConfigureArtwork(
                root,
                Resources.Load<Sprite>(ServingSpritePath),
                raycastTarget: true);
            DisableOutline(root.GetComponent<Outline>());
            DisableLegacyLayers(root.rectTransform);

            Image state = EnsureLayer(root.rectTransform, ServingStateLayerName);
            ConfigureFullStretch(state.rectTransform, 18f);
            state.sprite = null;
            state.type = Image.Type.Simple;
            state.color = Color.clear;
            state.raycastTarget = false;
            state.transform.SetSiblingIndex(0);
            return state;
        }

        internal static void ApplyTemporaryTable(Image root)
        {
            if (root == null) return;

            ConfigureArtwork(
                root,
                Resources.Load<Sprite>(TemporarySpritePath),
                raycastTarget: false);
            DisableOutline(root.GetComponent<Outline>());
            DisableLegacyLayers(root.rectTransform);
        }

        private static void ConfigureArtwork(Image root, Sprite sprite, bool raycastTarget)
        {
            if (sprite != null) root.sprite = sprite;

            root.type = Image.Type.Simple;
            root.preserveAspect = false;
            root.fillCenter = true;
            root.color = Color.white;
            root.raycastTarget = raycastTarget;
        }

        private static void DisableOutline(Outline outline)
        {
            if (outline != null) outline.enabled = false;
        }

        private static void DisableLegacyLayers(RectTransform parent)
        {
            for (int i = 0; i < LegacyLayerNames.Length; i++)
            {
                Transform layer = parent.Find(LegacyLayerNames[i]);
                if (layer != null) layer.gameObject.SetActive(false);
            }
        }

        private static Image EnsureLayer(RectTransform parent, string name)
        {
            Transform existing = parent.Find(name);
            Image image = existing != null ? existing.GetComponent<Image>() : null;
            if (image != null)
            {
                image.gameObject.SetActive(true);
                return image;
            }

            var layer = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            layer.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)layer.transform;
            rect.SetParent(parent, false);
            image = layer.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void ConfigureFullStretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.localScale = Vector3.one;
        }
    }
}
