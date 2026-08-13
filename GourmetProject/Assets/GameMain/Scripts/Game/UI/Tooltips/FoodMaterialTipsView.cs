using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 模块 1：纵向显示触发该食物的所有材质信息。
    /// </summary>
    public sealed class FoodMaterialTipsView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _content;
        [SerializeField] private Scrollbar _scrollbar;
        [SerializeField] private FoodTipCardView _itemPrefab;

        public void Bind(IReadOnlyList<FoodMaterialTipsEntry> materials)
        {
            if (!ValidateReferences())
            {
                return;
            }

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
                FoodTipCardView item = CreateItem(i);
                if (item != null)
                {
                    FoodMaterialTipsEntry material = uniqueMaterials[i];
                    item.Bind(material.Name, material.Desc);
                }
            }

            _scrollRect.vertical = false;
            _scrollbar.gameObject.SetActive(false);
            _scrollRect.viewport.offsetMax = Vector2.zero;
            _scrollRect.verticalNormalizedPosition = 1f;
            ResizeViewport();
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
            ValidateReferences();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_scrollRect, nameof(_scrollRect));
            valid &= ReportMissing(_content, nameof(_content));
            valid &= ReportMissing(_scrollbar, nameof(_scrollbar));
            valid &= ReportMissing(_itemPrefab, nameof(_itemPrefab));
            if (_scrollRect != null)
            {
                valid &= ReportMissing(_scrollRect.viewport, $"{nameof(_scrollRect)}.viewport");
                valid &= ReportMissing(_scrollRect.content, $"{nameof(_scrollRect)}.content");
            }

            return valid;
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

        private FoodTipCardView CreateItem(int index)
        {
            if (_itemPrefab == null)
            {
                ReportMissing(_itemPrefab, nameof(_itemPrefab));
                return null;
            }

            FoodTipCardView item = Instantiate(_itemPrefab, _content, false);
            item.name = $"Material_{index}";

            LayoutRebuilder.ForceRebuildLayoutImmediate(item.transform as RectTransform);
            return item;
        }

        private void ResizeViewport()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            RectTransform scrollRect = _scrollRect.transform as RectTransform;
            if (scrollRect == null)
            {
                return;
            }

            float height = Mathf.Max(_content.rect.height, LayoutUtility.GetPreferredHeight(_content));
            float expandedHeight = Mathf.Max(80f, height);

            scrollRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, expandedHeight);
            LayoutElement scrollLayout = scrollRect.gameObject.GetComponent<LayoutElement>();
            if (scrollLayout == null)
            {
                scrollLayout = scrollRect.gameObject.AddComponent<LayoutElement>();
            }

            scrollLayout.minHeight = expandedHeight;
            scrollLayout.preferredHeight = expandedHeight;
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodMaterialTipsView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
    }
}
