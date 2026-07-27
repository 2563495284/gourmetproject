using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜谱统一仓库视图：固定列数、矩形占格、自动紧凑排布与纵向拖拽浏览。
    /// 保留原类型名，避免已有 prefab 和页面引用迁移。
    /// </summary>
    public sealed class RecipeEditBookView : MonoBehaviour, IDropHandler
    {
        private const float LayoutTweenDuration = 0.2f;
        private static readonly Color DefaultFrameColor = new(0.82f, 0.74f, 0.58f, 1f);
        private static readonly Color DefaultSurfaceColor = new(0.075f, 0.09f, 0.095f, 1f);
        private static readonly Color DefaultGridColor = new(0.34f, 0.39f, 0.4f, 0.82f);

        [Header("Hierarchy")]
        [SerializeField] private RectTransform _dishContainer;
        [SerializeField] private RecipeWarehouseScrollRect _scrollRect;

        [Header("Warehouse Layout")]
        [SerializeField, Min(1)] private int _warehouseColumns = 12;
        [SerializeField, Min(0)] private int _warehouseTrailingRows = 10;
        [SerializeField, Min(24f)] private float _warehouseCellSize = 88f;
        [SerializeField, Min(0f)] private float _warehousePadding = 24f;
        [SerializeField, Min(0f)] private float _itemInset = 6f;
        [SerializeField, Min(0.5f)] private float _gridLineWidth = 2f;
        [SerializeField, Min(0f)] private float _frameThickness = 14f;

        [Header("Warehouse Style")]
        [SerializeField] private Color _frameColor = DefaultFrameColor;
        [SerializeField] private Color _surfaceColor = DefaultSurfaceColor;
        [SerializeField] private Color _gridColor = DefaultGridColor;

        private int _bookIndex;
        private Action<RecipeEditDishView, int, int> _onDishDropped;
        private GridLayoutGroup _legacyGrid;
        private RectTransform _viewport;
        private RecipeWarehouseGridGraphic _gridGraphic;
        private bool _scrollWired;
        private bool _hasLayout;
        private int _lastWarnedEffectiveColumns;
        private float _renderedCellSize;
        private float _renderedPadding;
        private float _renderedLineWidth;
        private readonly List<RecipeEditDishView> _layoutDishes = new();
        private readonly List<Vector2Int> _layoutSizes = new();

        public RectTransform DishContainer => _dishContainer;

        public RectTransform ViewportRect
        {
            get
            {
                ResolveLayout();
                return _viewport != null ? _viewport : transform as RectTransform;
            }
        }

        public Vector2 NormalizedPosition
        {
            get
            {
                ResolveLayout();
                return _scrollRect != null
                    ? new Vector2(0f, _scrollRect.verticalNormalizedPosition)
                    : new Vector2(0f, 1f);
            }
        }

        private void Awake()
        {
            ResolveLayout();
        }

        public void Bind(
            int bookIndex,
            Action<RecipeEditDishView, int, int> onDishDropped)
        {
            ResolveLayout();
            _bookIndex = bookIndex;
            _onDishDropped = onDishDropped;
            ApplyImmediateLayout();
            SetNormalizedPosition(new Vector2(0f, 1f));
        }

        public void SetNormalizedPosition(Vector2 normalizedPosition)
        {
            ResolveLayout();
            if (_scrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            _scrollRect.StopMovement();
            _scrollRect.horizontalNormalizedPosition = 0f;
            _scrollRect.verticalNormalizedPosition =
                Mathf.Clamp01(normalizedPosition.y);
        }

        public void OnDrop(PointerEventData eventData)
        {
            RecipeEditDishView dish = eventData?.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponentInParent<RecipeEditDishView>();
            if (dish != null)
            {
                _onDishDropped?.Invoke(dish, _bookIndex, DropIndex(eventData));
            }
        }

        public void ApplyImmediateLayout(RecipeEditDishView exclude = null)
        {
            ResolveLayout();
            if (_dishContainer == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Vector2 previousNormalized = _hasLayout && _scrollRect != null
                ? NormalizedPosition
                : new Vector2(0f, 1f);
            BuildLayoutInputs(exclude);
            Vector2 viewportSize = ViewportSize();
            RecipeWarehouseLayout.Result layout = RecipeWarehouseLayout.Pack(
                _layoutSizes,
                _warehouseColumns);
            WarnIfColumnsExpanded(layout.Columns);

            float widthScale = RecipeWarehouseLayout.ScaleForViewportWidth(
                layout.Columns,
                viewportSize.x,
                _warehouseCellSize,
                _warehousePadding);
            _renderedCellSize = _warehouseCellSize * widthScale;
            _renderedPadding = _warehousePadding * widthScale;
            _renderedLineWidth = _gridLineWidth * widthScale;
            float renderedInset = _itemInset * widthScale;
            int contentRows = RecipeWarehouseLayout.ContentRows(
                layout.Rows,
                _warehouseTrailingRows,
                viewportSize.y,
                _renderedCellSize,
                _renderedPadding);
            float designWidth = _warehousePadding * 2f
                + layout.Columns * _warehouseCellSize;
            float contentWidth = viewportSize.x > 0.01f
                ? viewportSize.x
                : designWidth;
            float contentHeight = _renderedPadding * 2f
                + contentRows * _renderedCellSize;
            _dishContainer.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                contentWidth);
            _dishContainer.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                contentHeight);
            _dishContainer.anchoredPosition = new Vector2(
                0f,
                _dishContainer.anchoredPosition.y);

            int count = Mathf.Min(_layoutDishes.Count, layout.Placements.Count);
            for (int i = 0; i < count; i++)
            {
                RecipeWarehouseLayout.Placement placement = layout.Placements[i];
                RectTransform rect = (RectTransform)_layoutDishes[i].transform;
                ConfigureDishRect(rect);
                rect.anchoredPosition = new Vector2(
                    _renderedPadding
                        + placement.Position.x * _renderedCellSize
                        + renderedInset * 0.5f,
                    -_renderedPadding
                        - placement.Position.y * _renderedCellSize
                        - renderedInset * 0.5f);
                rect.sizeDelta = new Vector2(
                    Mathf.Max(1f, placement.Size.x * _renderedCellSize - renderedInset),
                    Mathf.Max(1f, placement.Size.y * _renderedCellSize - renderedInset));
            }

            ConfigureGridGraphic();
            Canvas.ForceUpdateCanvases();
            _hasLayout = true;
            SetNormalizedPosition(previousNormalized);
        }

        public void FitSlotsWithinView()
        {
            ApplyImmediateLayout();
        }

        public void AnimateCompaction(RecipeEditDishView exclude = null)
        {
            AnimateRelayout(exclude);
        }

        public void AnimateInsertionGap(int insertIndex, RecipeEditDishView exclude = null)
        {
            AnimateRelayout(exclude);
        }

        public void ScrollToIndex(int index, float duration, Action onComplete = null)
        {
            ResolveLayout();
            RecipeEditDishView[] dishes = Dishes();
            if (_scrollRect == null || index < 0 || index >= dishes.Length)
            {
                onComplete?.Invoke();
                return;
            }

            RectTransform target = (RectTransform)dishes[index].transform;
            Vector2 destination = NormalizedPositionForRect(target);
            _scrollRect.StopMovement();
            DOTween.Kill(_scrollRect);
            if (duration <= 0f)
            {
                SetNormalizedPosition(destination);
                onComplete?.Invoke();
                return;
            }

            DOTween.To(
                    () => _scrollRect.verticalNormalizedPosition,
                    value => _scrollRect.verticalNormalizedPosition = value,
                    destination.y,
                    duration)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(_scrollRect)
                .OnComplete(() => onComplete?.Invoke());
        }

        public Vector3 SlotWorldCenter(int index)
        {
            RecipeEditDishView[] dishes = Dishes();
            return index >= 0 && index < dishes.Length
                ? ((RectTransform)dishes[index].transform).TransformPoint(
                    ((RectTransform)dishes[index].transform).rect.center)
                : transform.position;
        }

        public Vector3 SlotWorldCenterAfterScrollToIndex(int index)
        {
            ScrollToIndex(index, 0f);
            return SlotWorldCenter(index);
        }

        public int CurrentDishCount(RecipeEditDishView exclude = null)
        {
            int count = 0;
            RecipeEditDishView[] dishes = Dishes();
            for (int i = 0; i < dishes.Length; i++)
            {
                if (dishes[i] != null && dishes[i] != exclude)
                {
                    count++;
                }
            }

            return count;
        }

        private void AnimateRelayout(RecipeEditDishView exclude)
        {
            ResolveLayout();
            var starts = new Dictionary<RectTransform, Vector2>();
            RecipeEditDishView[] dishes = Dishes();
            for (int i = 0; i < dishes.Length; i++)
            {
                if (dishes[i] != null && dishes[i] != exclude)
                {
                    RectTransform rect = (RectTransform)dishes[i].transform;
                    starts[rect] = rect.anchoredPosition;
                }
            }

            ApplyImmediateLayout(exclude);
            foreach (KeyValuePair<RectTransform, Vector2> pair in starts)
            {
                RectTransform rect = pair.Key;
                if (rect == null)
                {
                    continue;
                }

                Vector2 target = rect.anchoredPosition;
                rect.anchoredPosition = pair.Value;
                DOTween.Kill(rect);
                DOTween.To(
                        () => rect.anchoredPosition,
                        value => rect.anchoredPosition = value,
                        target,
                        LayoutTweenDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetTarget(rect)
                    .SetLink(rect.gameObject);
            }
        }

        private void ResolveLayout()
        {
            if (_dishContainer == null)
            {
                _dishContainer = transform.Find("DishContainer") as RectTransform;
            }

            if (_dishContainer == null)
            {
                _dishContainer = transform as RectTransform;
            }

            if (_dishContainer == null)
            {
                return;
            }

            _legacyGrid ??= _dishContainer.GetComponent<GridLayoutGroup>();
            if (_legacyGrid != null)
            {
                _legacyGrid.enabled = false;
            }

            EnsureScrollRect();
            ConfigureContentRect();
            EnsureGridGraphic();
        }

        private void EnsureScrollRect()
        {
            if (_scrollWired)
            {
                return;
            }

            _scrollWired = true;
            RectTransform root = transform as RectTransform;
            if (root == null)
            {
                return;
            }

            Image frame = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            frame.color = _frameColor;
            frame.raycastTarget = true;

            _viewport = transform.Find("WarehouseViewport") as RectTransform;
            if (_viewport == null)
            {
                var viewportObject = new GameObject(
                    "WarehouseViewport",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(RectMask2D));
                viewportObject.layer = gameObject.layer;
                _viewport = viewportObject.GetComponent<RectTransform>();
                _viewport.SetParent(transform, false);
                _viewport.SetAsFirstSibling();
            }

            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.pivot = new Vector2(0.5f, 0.5f);
            _viewport.offsetMin = Vector2.one * _frameThickness;
            _viewport.offsetMax = -Vector2.one * _frameThickness;
            Image viewportImage = _viewport.GetComponent<Image>();
            viewportImage.color = _surfaceColor;
            viewportImage.raycastTarget = true;

            if (_dishContainer.parent != _viewport)
            {
                _dishContainer.SetParent(_viewport, false);
            }

            _scrollRect = GetComponent<RecipeWarehouseScrollRect>();
            if (_scrollRect == null)
            {
                ScrollRect legacyScrollRect = GetComponent<ScrollRect>();
                if (legacyScrollRect != null)
                {
                    DestroyComponent(legacyScrollRect);
                }

                _scrollRect = gameObject.AddComponent<RecipeWarehouseScrollRect>();
            }

            _scrollRect.content = _dishContainer;
            _scrollRect.viewport = _viewport;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = 0.12f;
            _scrollRect.scrollSensitivity = 32f;
            _scrollRect.horizontalScrollbar = null;
            _scrollRect.verticalScrollbar = null;
        }

        private void ConfigureContentRect()
        {
            bool alreadyTopLeft = _dishContainer.anchorMin == new Vector2(0f, 1f)
                && _dishContainer.anchorMax == new Vector2(0f, 1f)
                && _dishContainer.pivot == new Vector2(0f, 1f);
            _dishContainer.anchorMin = new Vector2(0f, 1f);
            _dishContainer.anchorMax = new Vector2(0f, 1f);
            _dishContainer.pivot = new Vector2(0f, 1f);
            if (!alreadyTopLeft)
            {
                _dishContainer.anchoredPosition = Vector2.zero;
            }
        }

        private void EnsureGridGraphic()
        {
            if (_gridGraphic != null)
            {
                return;
            }

            Transform existing = _dishContainer.Find("WarehouseGrid");
            if (existing != null)
            {
                _gridGraphic = existing.GetComponent<RecipeWarehouseGridGraphic>();
            }

            if (_gridGraphic == null)
            {
                var gridObject = new GameObject(
                    "WarehouseGrid",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(RecipeWarehouseGridGraphic));
                gridObject.layer = gameObject.layer;
                RectTransform gridRect = gridObject.GetComponent<RectTransform>();
                gridRect.SetParent(_dishContainer, false);
                gridRect.anchorMin = Vector2.zero;
                gridRect.anchorMax = Vector2.one;
                gridRect.offsetMin = Vector2.zero;
                gridRect.offsetMax = Vector2.zero;
                _gridGraphic = gridObject.GetComponent<RecipeWarehouseGridGraphic>();
            }

            _gridGraphic.transform.SetAsFirstSibling();
            ConfigureGridGraphic();
        }

        private void ConfigureGridGraphic()
        {
            if (_gridGraphic == null)
            {
                return;
            }

            _gridGraphic.Configure(
                _renderedCellSize > 0f ? _renderedCellSize : _warehouseCellSize,
                _renderedPadding > 0f ? _renderedPadding : _warehousePadding,
                _renderedLineWidth > 0f ? _renderedLineWidth : _gridLineWidth,
                _surfaceColor,
                _gridColor);
            _gridGraphic.transform.SetAsFirstSibling();
        }

        private void BuildLayoutInputs(RecipeEditDishView exclude)
        {
            _layoutDishes.Clear();
            _layoutSizes.Clear();
            RecipeEditDishView[] dishes = Dishes();
            for (int i = 0; i < dishes.Length; i++)
            {
                RecipeEditDishView dish = dishes[i];
                if (dish == null || dish == exclude)
                {
                    continue;
                }

                _layoutDishes.Add(dish);
                Vector2Int size = dish.DisplayedGridSize;
                _layoutSizes.Add(new Vector2Int(
                    Mathf.Max(1, size.x),
                    Mathf.Max(1, size.y)));
            }
        }

        private void WarnIfColumnsExpanded(int effectiveColumns)
        {
            int configuredColumns = Mathf.Max(1, _warehouseColumns);
            if (effectiveColumns <= configuredColumns)
            {
                _lastWarnedEffectiveColumns = 0;
                return;
            }

            if (_lastWarnedEffectiveColumns == effectiveColumns)
            {
                return;
            }

            _lastWarnedEffectiveColumns = effectiveColumns;
            Debug.LogWarning(
                $"Warehouse width expanded from {configuredColumns} to {effectiveColumns} columns "
                + "because at least one dish is wider than the configured warehouse.",
                this);
        }

        private int DropIndex(PointerEventData eventData)
        {
            if (_dishContainer == null || eventData == null)
            {
                return CurrentDishCount();
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : eventData.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _dishContainer,
                    eventData.position,
                    camera,
                    out Vector2 local))
            {
                return CurrentDishCount();
            }

            RecipeEditDishView[] dishes = Dishes();
            for (int i = 0; i < dishes.Length; i++)
            {
                RectTransform rect = (RectTransform)dishes[i].transform;
                if (rect.rect.Contains(rect.InverseTransformPoint(
                        _dishContainer.TransformPoint(local))))
                {
                    return i;
                }
            }

            return dishes.Length;
        }

        private Vector2 NormalizedPositionForRect(RectTransform target)
        {
            Vector2 viewportSize = ViewportSize();
            Vector2 contentSize = _dishContainer.rect.size;
            float scrollableHeight = Mathf.Max(0f, contentSize.y - viewportSize.y);
            float targetTop = Mathf.Max(0f, -target.anchoredPosition.y);
            return new Vector2(
                0f,
                scrollableHeight > 0.01f
                    ? 1f - Mathf.Clamp01(targetTop / scrollableHeight)
                    : 1f);
        }

        private Vector2 ViewportSize()
        {
            RectTransform viewport = _viewport != null
                ? _viewport
                : transform as RectTransform;
            return viewport != null ? viewport.rect.size : Vector2.zero;
        }

        private static void ConfigureDishRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private RecipeEditDishView[] Dishes()
        {
            return _dishContainer == null
                ? Array.Empty<RecipeEditDishView>()
                : _dishContainer.GetComponentsInChildren<RecipeEditDishView>(false);
        }

        private static void DestroyComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                DestroyImmediate(component);
            }
        }
    }
}
