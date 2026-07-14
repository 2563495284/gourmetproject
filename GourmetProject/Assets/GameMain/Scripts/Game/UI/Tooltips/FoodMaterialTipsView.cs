using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 模块 1：显示触发该食物的所有材质信息。超过 3 条时启用滚动。
    /// </summary>
    public sealed class FoodMaterialTipsView : MonoBehaviour
    {
        public const int VisibleMaterialCount = 3;

        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _titleText;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _content;
        [SerializeField] private Scrollbar _scrollbar;

        public void Bind(IReadOnlyList<FoodMaterialTipsEntry> materials)
        {
            EnsureStructure();
            FoodTipUiUtility.ClearChildren(_content);

            IReadOnlyList<FoodMaterialTipsEntry> uniqueMaterials = UniqueMaterials(materials);
            int count = uniqueMaterials.Count;
            bool visible = count > 0;
            gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                BuildMaterialBlock(uniqueMaterials[i], i);
            }

            bool needsScroll = count > VisibleMaterialCount;
            _scrollRect.vertical = needsScroll;
            _scrollbar.gameObject.SetActive(needsScroll);
            ApplyScrollbarSpacing(needsScroll);
            _scrollRect.verticalNormalizedPosition = 1f;
            ResizeViewport(Mathf.Min(count, VisibleMaterialCount));
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        private void Awake()
        {
            EnsureStructure();
        }

        private void Reset()
        {
            EnsureStructure();
        }

        private void EnsureStructure()
        {
            RectTransform root = FoodTipUiUtility.EnsureRect(gameObject);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 300f);
            FoodTipUiUtility.EnsurePanelImage(gameObject, new Color(1f, 1f, 1f, 0.96f));

            _canvasGroup ??= gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            var rootLayout = gameObject.GetComponent<VerticalLayoutGroup>();
            if (rootLayout == null)
            {
                rootLayout = gameObject.AddComponent<VerticalLayoutGroup>();
            }

            rootLayout.padding = new RectOffset(14, 14, 12, 14);
            rootLayout.spacing = 10f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var fitter = gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titleText = FoodTipUiUtility.EnsureTextChild(root, _titleText, "Title", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            _titleText.text = "材质";

            if (_scrollRect == null)
            {
                RectTransform scrollRoot = root.Find("Scroll") as RectTransform;
                if (scrollRoot == null)
                {
                    scrollRoot = FoodTipUiUtility.CreateChild(root, "Scroll");
                }

                _scrollRect = scrollRoot.gameObject.GetComponent<ScrollRect>() ?? scrollRoot.gameObject.AddComponent<ScrollRect>();
                Image scrollImage = scrollRoot.gameObject.GetComponent<Image>() ?? scrollRoot.gameObject.AddComponent<Image>();
                scrollImage.color = Color.clear;
                scrollImage.raycastTarget = false;

                RectTransform viewport = scrollRoot.Find("Viewport") as RectTransform;
                if (viewport == null)
                {
                    viewport = FoodTipUiUtility.CreateChild(scrollRoot, "Viewport");
                }

                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = Vector2.zero;
                Mask mask = viewport.gameObject.GetComponent<Mask>() ?? viewport.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                Image viewportImage = viewport.gameObject.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
                viewportImage.color = Color.white;
                viewportImage.raycastTarget = false;

                _content = viewport.Find("Content") as RectTransform;
                if (_content == null)
                {
                    _content = FoodTipUiUtility.CreateChild(viewport, "Content");
                }

                _scrollRect.viewport = viewport;
                _scrollRect.content = _content;
                _scrollRect.horizontal = false;
                _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            }

            if (_content != null)
            {
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.anchoredPosition = Vector2.zero;
                _content.offsetMin = new Vector2(0f, _content.offsetMin.y);
                _content.offsetMax = new Vector2(0f, _content.offsetMax.y);

                var contentLayout = _content.gameObject.GetComponent<VerticalLayoutGroup>();
                if (contentLayout == null)
                {
                    contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
                }

                contentLayout.spacing = 14f;
                contentLayout.childControlWidth = true;
                contentLayout.childControlHeight = true;
                contentLayout.childForceExpandWidth = true;
                contentLayout.childForceExpandHeight = false;

                var contentFitter = _content.gameObject.GetComponent<ContentSizeFitter>();
                if (contentFitter == null)
                {
                    contentFitter = _content.gameObject.AddComponent<ContentSizeFitter>();
                }

                contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            if (_scrollbar == null)
            {
                RectTransform bar = _scrollRect.transform.Find("Scrollbar") as RectTransform;
                if (bar == null)
                {
                    bar = root.Find("Scrollbar") as RectTransform;
                }

                if (bar == null)
                {
                    bar = FoodTipUiUtility.CreateChild(_scrollRect.transform as RectTransform, "Scrollbar");
                }

                _scrollbar = bar.gameObject.GetComponent<Scrollbar>() ?? bar.gameObject.AddComponent<Scrollbar>();
                _scrollbar.direction = Scrollbar.Direction.BottomToTop;
                _scrollbar.gameObject.SetActive(false);
            }

            ConfigureScrollbar();
            _scrollRect.verticalScrollbar = _scrollbar;
            ApplyScrollbarSpacing(_scrollbar != null && _scrollbar.gameObject.activeSelf);
        }

        private void ConfigureScrollbar()
        {
            if (_scrollRect == null || _scrollbar == null)
            {
                return;
            }

            RectTransform bar = _scrollbar.transform as RectTransform;
            RectTransform scrollRoot = _scrollRect.transform as RectTransform;
            if (bar == null || scrollRoot == null)
            {
                return;
            }

            if (bar.parent != scrollRoot)
            {
                bar.SetParent(scrollRoot, false);
            }

            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.anchoredPosition = Vector2.zero;
            bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 18f);
            bar.offsetMin = new Vector2(-18f, 0f);
            bar.offsetMax = Vector2.zero;

            Image barImage = bar.gameObject.GetComponent<Image>() ?? bar.gameObject.AddComponent<Image>();
            barImage.color = new Color(0f, 0f, 0f, 0.12f);
            barImage.raycastTarget = true;

            RectTransform sliding = bar.Find("Sliding Area") as RectTransform;
            if (sliding == null)
            {
                sliding = FoodTipUiUtility.CreateChild(bar, "Sliding Area");
            }

            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(3f, 3f);
            sliding.offsetMax = new Vector2(-3f, -3f);

            RectTransform handle = sliding.Find("Handle") as RectTransform;
            if (handle == null)
            {
                handle = FoodTipUiUtility.CreateChild(sliding, "Handle");
            }

            Image handleImage = handle.gameObject.GetComponent<Image>() ?? handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.95f, 0.31f, 0.34f, 0.95f);
            handleImage.raycastTarget = true;
            _scrollbar.targetGraphic = handleImage;
            _scrollbar.handleRect = handle;

            RectTransform viewport = _scrollRect.viewport;
            ApplyScrollbarSpacing(_scrollbar.gameObject.activeSelf);
        }

        private IReadOnlyList<FoodMaterialTipsEntry> UniqueMaterials(IReadOnlyList<FoodMaterialTipsEntry> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                return System.Array.Empty<FoodMaterialTipsEntry>();
            }

            var result = new List<FoodMaterialTipsEntry>();
            var seen = new HashSet<string>();
            foreach (FoodMaterialTipsEntry material in materials)
            {
                if (material == null)
                {
                    continue;
                }

                string key = !string.IsNullOrEmpty(material.Id) ? material.Id : material.Name;
                if (string.IsNullOrEmpty(key) || !seen.Add(key))
                {
                    continue;
                }

                result.Add(material);
            }

            return result;
        }

        private void BuildMaterialBlock(FoodMaterialTipsEntry material, int index)
        {
            RectTransform block = FoodTipUiUtility.CreateChild(_content, $"Material_{index}");

            var layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 0, 0);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = block.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text nameText = FoodTipUiUtility.EnsureTextChild(block, null, "Name", 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            nameText.text = material.Name ?? string.Empty;

            Text descText = FoodTipUiUtility.EnsureTextChild(block, null, "Desc", 18, FontStyle.Normal, TextAnchor.UpperCenter);
            descText.text = material.Desc ?? string.Empty;
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descText.verticalOverflow = VerticalWrapMode.Overflow;

            LayoutElement blockLayout = block.gameObject.AddComponent<LayoutElement>();
            blockLayout.minHeight = 72f;
            blockLayout.preferredHeight = -1f;

            LayoutRebuilder.ForceRebuildLayoutImmediate(block);
        }

        private void ApplyScrollbarSpacing(bool needsScroll)
        {
            RectTransform viewport = _scrollRect != null ? _scrollRect.viewport : null;
            if (viewport != null)
            {
                viewport.offsetMax = needsScroll ? new Vector2(-24f, 0f) : Vector2.zero;
            }
        }

        private void ResizeViewport(int visibleCount)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            RectTransform scrollRect = _scrollRect.transform as RectTransform;
            if (scrollRect == null)
            {
                return;
            }

            float contentHeight = Mathf.Max(_content.rect.height, LayoutUtility.GetPreferredHeight(_content));
            float height = contentHeight;
            if (visibleCount > 0 && _content.childCount > visibleCount)
            {
                var layout = _content.GetComponent<VerticalLayoutGroup>();
                float spacing = layout != null ? layout.spacing : 0f;
                height = 0f;
                for (int i = 0; i < visibleCount; i++)
                {
                    RectTransform child = _content.GetChild(i) as RectTransform;
                    if (child != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(child);
                        height += Mathf.Max(child.rect.height, LayoutUtility.GetPreferredHeight(child));
                    }
                }

                height += Mathf.Max(0, visibleCount - 1) * spacing;
            }

            scrollRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(80f, height));
            LayoutElement scrollLayout = scrollRect.gameObject.GetComponent<LayoutElement>() ?? scrollRect.gameObject.AddComponent<LayoutElement>();
            scrollLayout.minHeight = Mathf.Max(80f, height);
            scrollLayout.preferredHeight = Mathf.Max(80f, height);
        }
    }
}
