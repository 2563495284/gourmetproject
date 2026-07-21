using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Scoring;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Tooltips
{
    /// <summary>
    /// 食物 hover Tips 组合器：只负责组合/定位 1、2、3 等独立模块。
    /// </summary>
    public sealed class FoodTipsView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private FoodMaterialTipsView _materialsView;
        [SerializeField] private FoodScoreTipsView _scoreView;
        [SerializeField] private FoodSummaryTipsView _summaryView;
        [SerializeField] private RectTransform _flavorDetailsRoot;
        [SerializeField] private RectTransform _specialTagsRoot;
        [SerializeField] private RectTransform _transferredSubSkillsRoot;
        [SerializeField] private FoodTipCardView _infoCardPrefab;

        [Header("Layout")]
        [SerializeField] private float _targetGap = 18f;
        [SerializeField] private float _screenPadding = 16f;
        [SerializeField] private float _detailGap = 10f;

        public FoodMaterialTipsView MaterialsView
        {
            get
            {
                ValidateReferences();
                return _materialsView;
            }
        }

        public FoodScoreTipsView ScoreView
        {
            get
            {
                ValidateReferences();
                return _scoreView;
            }
        }

        public FoodSummaryTipsView SummaryView
        {
            get
            {
                ValidateReferences();
                return _summaryView;
            }
        }

        public void Bind(DishInstance dish, DiningTable table, GameplayDatabase db, ScoreResult scoreResult = null)
        {
            Bind(FoodTipsDataFactory.Build(dish, table, db, scoreResult));
        }

        public void Bind(FoodTipsData data)
        {
            if (!ValidateReferences())
            {
                return;
            }

            data ??= new FoodTipsData(null, null, null, null, null, null);

            _materialsView.Bind(data.Materials);
            _scoreView.Bind(data.Score);
            _summaryView.Bind(data.Summary);
            BuildInfoCards(_flavorDetailsRoot, data.FlavorDetails, "FlavorDetail");
            BuildInfoCards(_specialTagsRoot, data.SpecialTags, "SpecialTag");
            BuildInfoCards(_transferredSubSkillsRoot, data.TransferredSubSkills, "TransferredSubSkill");
        }

        public void BindMaterialsOnly(IReadOnlyList<FoodMaterialTipsEntry> materials)
        {
            if (!ValidateReferences())
            {
                return;
            }

            _materialsView.Bind(materials);
            _scoreView.Hide();
            _summaryView.Hide();
            FoodTipUiUtility.ClearChildren(_flavorDetailsRoot);
            FoodTipUiUtility.ClearChildren(_specialTagsRoot);
            FoodTipUiUtility.ClearChildren(_transferredSubSkillsRoot);
            _flavorDetailsRoot.gameObject.SetActive(false);
            _specialTagsRoot.gameObject.SetActive(false);
            _transferredSubSkillsRoot.gameObject.SetActive(false);
        }

        public void Show()
        {
            ValidateReferences();
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

        public void PlaceAroundWorldBounds(Bounds worldBounds, Camera worldCamera, Canvas canvas)
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }

            if (worldCamera == null)
            {
                return;
            }

            canvas ??= GetComponentInParent<Canvas>();
            RectTransform parent = transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            RectTransform canvasRect = parent.parent as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Rect targetRect = WorldBoundsToLocalRect(worldBounds, worldCamera, canvasRect, uiCamera);
            Canvas.ForceUpdateCanvases();

            PlaceLeft(_materialsView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceAbove(_scoreView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceSummaryGroup(targetRect, canvasRect);
        }

        public void PlaceAroundRectTransform(RectTransform target, Canvas canvas)
        {
            if (target == null)
            {
                return;
            }

            canvas ??= GetComponentInParent<Canvas>();
            RectTransform parent = transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            RectTransform canvasRect = parent.parent as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            Rect targetRect = RectTransformToLocalRect(target, canvasRect);
            Canvas.ForceUpdateCanvases();

            PlaceLeft(_materialsView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceAbove(_scoreView.transform as RectTransform, targetRect, canvasRect.rect);
            PlaceSummaryGroup(targetRect, canvasRect);
        }

        private void PlaceSummaryGroup(Rect targetRect, RectTransform canvasRect)
        {
            RectTransform summaryRect = _summaryView.transform as RectTransform;
            PlaceRightTop(summaryRect, targetRect, canvasRect.rect);

            // Detail lists belong to the summary card. Keep their intended relative positions
            // first, then clamp the complete group so a long flavor list moves the whole column
            // upward instead of being clamped into (and overlapping) the summary card.
            PlaceBelow(_flavorDetailsRoot, summaryRect);
            PlaceAbove(_specialTagsRoot, summaryRect);
            PlaceRight(_transferredSubSkillsRoot, summaryRect);
            Canvas.ForceUpdateCanvases();
            ClampSummaryGroupToBounds(canvasRect, canvasRect.rect, summaryRect);
        }

        private void Awake()
        {
            ValidateReferences();
            Hide();
        }

        private void Reset()
        {
            ValidateReferences();
        }

        private bool ValidateReferences()
        {
            bool valid = true;
            valid &= ReportMissing(_canvasGroup, nameof(_canvasGroup));
            valid &= ReportMissing(_materialsView, nameof(_materialsView));
            valid &= ReportMissing(_scoreView, nameof(_scoreView));
            valid &= ReportMissing(_summaryView, nameof(_summaryView));
            valid &= ReportMissing(_flavorDetailsRoot, nameof(_flavorDetailsRoot));
            valid &= ReportMissing(_specialTagsRoot, nameof(_specialTagsRoot));
            valid &= ReportMissing(_transferredSubSkillsRoot, nameof(_transferredSubSkillsRoot));
            valid &= ReportMissing(_infoCardPrefab, nameof(_infoCardPrefab));
            return valid;
        }

        private void BuildInfoCards(RectTransform root, IReadOnlyList<FoodInfoEntry> entries, string prefix)
        {
            FoodTipUiUtility.ClearChildren(root);
            int count = entries != null ? entries.Count : 0;
            root.gameObject.SetActive(count > 0);
            for (int i = 0; i < count; i++)
            {
                FoodInfoEntry entry = entries[i];
                FoodTipCardView card = Instantiate(_infoCardPrefab, root, false);
                card.name = $"{prefix}_{i}";
                card.Bind(entry.Title, entry.Desc);
            }
        }

        private bool ReportMissing(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(FoodTipsView)} on '{name}' is missing prefab reference '{fieldName}'.", this);
            return false;
        }
        private Rect WorldBoundsToLocalRect(Bounds bounds, Camera worldCamera, RectTransform parent, Camera uiCamera)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] world =
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
            };

            bool has = false;
            Vector2 localMin = Vector2.zero;
            Vector2 localMax = Vector2.zero;
            foreach (Vector3 point in world)
            {
                Vector2 screen = worldCamera.WorldToScreenPoint(point);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out Vector2 local))
                {
                    continue;
                }

                if (!has)
                {
                    localMin = localMax = local;
                    has = true;
                }
                else
                {
                    localMin = Vector2.Min(localMin, local);
                    localMax = Vector2.Max(localMax, local);
                }
            }

            return has
                ? Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y)
                : new Rect(Vector2.zero, Vector2.one);
        }

        private Rect RectTransformToLocalRect(RectTransform target, RectTransform parent)
        {
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);

            Vector2 localMin = parent.InverseTransformPoint(corners[0]);
            Vector2 localMax = localMin;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 local = parent.InverseTransformPoint(corners[i]);
                localMin = Vector2.Min(localMin, local);
                localMax = Vector2.Max(localMax, local);
            }

            return Rect.MinMaxRect(localMin.x, localMin.y, localMax.x, localMax.y);
        }

        private void PlaceLeft(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.xMin - _targetGap - size.x * 0.5f, target.center.y);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceAbove(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.center.x, target.yMax + _targetGap + size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceRightTop(RectTransform rect, Rect target, Rect bounds)
        {
            if (rect == null || !rect.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 center = new Vector2(target.xMax + _targetGap + size.x * 0.5f, target.yMax - size.y * 0.5f);
            SetCenter(rect, Clamp(center, size, bounds));
        }

        private void PlaceBelow(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x, anchorCenter.y - anchorSize.y * 0.5f - _detailGap - size.y * 0.5f);
            SetCenter(rect, center);
        }

        private void PlaceAbove(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x, anchorCenter.y + anchorSize.y * 0.5f + _detailGap + size.y * 0.5f);
            SetCenter(rect, center);
        }

        private void PlaceRight(RectTransform rect, RectTransform anchor)
        {
            if (rect == null || anchor == null || !rect.gameObject.activeSelf || !anchor.gameObject.activeSelf)
            {
                return;
            }

            Vector2 size = PreferredSize(rect);
            Vector2 anchorCenter = RectCenterInParent(anchor);
            Vector2 anchorSize = PreferredSize(anchor);
            Vector2 center = new Vector2(anchorCenter.x + anchorSize.x * 0.5f + _detailGap + size.x * 0.5f, anchorCenter.y);
            SetCenter(rect, center);
        }

        private void ClampSummaryGroupToBounds(RectTransform canvasRect, Rect bounds, RectTransform summaryRect)
        {
            RectTransform[] group =
            {
                summaryRect,
                _flavorDetailsRoot,
                _specialTagsRoot,
                _transferredSubSkillsRoot,
            };

            bool hasVisibleRect = false;
            Rect visibleRect = default;
            for (int i = 0; i < group.Length; i++)
            {
                RectTransform rect = group[i];
                if (rect == null || !rect.gameObject.activeSelf)
                {
                    continue;
                }

                Rect childRect = RectTransformToLocalRect(rect, canvasRect);
                if (!hasVisibleRect)
                {
                    visibleRect = childRect;
                    hasVisibleRect = true;
                }
                else
                {
                    visibleRect = Rect.MinMaxRect(
                        Mathf.Min(visibleRect.xMin, childRect.xMin),
                        Mathf.Min(visibleRect.yMin, childRect.yMin),
                        Mathf.Max(visibleRect.xMax, childRect.xMax),
                        Mathf.Max(visibleRect.yMax, childRect.yMax));
                }
            }

            if (!hasVisibleRect)
            {
                return;
            }

            float minX = bounds.xMin + _screenPadding;
            float maxX = bounds.xMax - _screenPadding;
            float minY = bounds.yMin + _screenPadding;
            float maxY = bounds.yMax - _screenPadding;
            Vector2 offset = Vector2.zero;

            if (visibleRect.width > maxX - minX)
            {
                offset.x = bounds.center.x - visibleRect.center.x;
            }
            else if (visibleRect.xMax > maxX)
            {
                offset.x = maxX - visibleRect.xMax;
            }
            else if (visibleRect.xMin < minX)
            {
                offset.x = minX - visibleRect.xMin;
            }

            if (visibleRect.height > maxY - minY)
            {
                offset.y = bounds.center.y - visibleRect.center.y;
            }
            else if (visibleRect.yMax > maxY)
            {
                offset.y = maxY - visibleRect.yMax;
            }
            else if (visibleRect.yMin < minY)
            {
                offset.y = minY - visibleRect.yMin;
            }

            for (int i = 0; i < group.Length; i++)
            {
                RectTransform rect = group[i];
                if (rect != null && rect.gameObject.activeSelf)
                {
                    rect.anchoredPosition += offset;
                }
            }
        }

        private Vector2 PreferredSize(RectTransform rect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            Vector2 size = rect.rect.size;
            size.y = Mathf.Max(size.y, LayoutUtility.GetPreferredHeight(rect));
            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            return size;
        }

        private Vector2 Clamp(Vector2 center, Vector2 size, Rect bounds)
        {
            float halfW = size.x * 0.5f;
            float halfH = size.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(center.x, bounds.xMin + halfW + _screenPadding, bounds.xMax - halfW - _screenPadding),
                Mathf.Clamp(center.y, bounds.yMin + halfH + _screenPadding, bounds.yMax - halfH - _screenPadding));
        }

        private static void SetCenter(RectTransform rect, Vector2 center)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
        }

        private static Vector2 RectCenterInParent(RectTransform rect)
        {
            RectTransform parent = rect.parent as RectTransform;
            if (parent == null)
            {
                return rect.anchoredPosition;
            }

            Vector3 worldCenter = rect.TransformPoint(rect.rect.center);
            return parent.InverseTransformPoint(worldCenter);
        }
    }
}
