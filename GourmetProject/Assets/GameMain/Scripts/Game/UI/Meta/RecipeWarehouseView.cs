using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜谱统一仓库视图：固定列数、矩形占格、自动紧凑排布与纵向浏览。
    /// </summary>
    public sealed class RecipeWarehouseView : MonoBehaviour
    {
        private static readonly Color DefaultSurfaceColor =
            new(0.075f, 0.09f, 0.095f, 1f);
        private static readonly Color DefaultGridColor =
            new(0.34f, 0.39f, 0.4f, 0.82f);

        [Header("Hierarchy")]
        [SerializeField] private RectTransform _dishContainer;
        [SerializeField] private RecipeWarehouseScrollRect _scrollRect;
        [SerializeField] private RecipeWarehouseGridGraphic _gridGraphic;

        [Header("Warehouse Layout")]
        [SerializeField, Min(1)] private int _warehouseColumns = 12;
        [SerializeField, Min(0)] private int _warehouseTrailingRows = 10;
        [SerializeField, Min(24f)] private float _warehouseCellSize = 88f;
        [SerializeField, Min(0f)] private float _warehousePadding = 24f;
        [SerializeField, Min(0f)] private float _itemInset = 6f;
        [SerializeField, Min(0.5f)] private float _gridLineWidth = 2f;

        [Header("Warehouse Style")]
        [SerializeField] private Color _surfaceColor = DefaultSurfaceColor;
        [SerializeField] private Color _gridColor = DefaultGridColor;

        private readonly List<RecipeEditDishView> _layoutDishes = new();
        private readonly List<Vector2Int> _layoutSizes = new();
        private bool _hasLayout;
        private bool _refreshing;
        private int _lastWarnedEffectiveColumns;
        private Vector2 _lastViewportSize = new(-1f, -1f);

        public RectTransform DishContainer => _dishContainer;

        public float VerticalNormalizedPosition =>
            _scrollRect != null ? _scrollRect.verticalNormalizedPosition : 1f;

        private void Awake()
        {
            ValidateHierarchy();
        }

        private void OnEnable()
        {
            RefreshLayout();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || _refreshing)
            {
                return;
            }

            Vector2 size = ViewportSize();
            if ((size - _lastViewportSize).sqrMagnitude > 0.01f)
            {
                RefreshLayout();
            }
        }

        public void SetVerticalNormalizedPosition(float normalizedPosition)
        {
            if (_scrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            _scrollRect.StopMovement();
            _scrollRect.horizontalNormalizedPosition = 0f;
            _scrollRect.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
        }

        public void RefreshLayout()
        {
            if (_refreshing || !ValidateHierarchy())
            {
                return;
            }

            _refreshing = true;
            try
            {
                Canvas.ForceUpdateCanvases();
                float previousNormalized = _hasLayout
                    ? VerticalNormalizedPosition
                    : 1f;
                BuildLayoutInputs();
                Vector2 viewportSize = ViewportSize();
                _lastViewportSize = viewportSize;
                RecipeWarehouseLayout.Result layout = RecipeWarehouseLayout.Pack(
                    _layoutSizes,
                    _warehouseColumns);
                WarnIfColumnsExpanded(layout.Columns);

                float widthScale = RecipeWarehouseLayout.ScaleForViewportWidth(
                    layout.Columns,
                    viewportSize.x,
                    _warehouseCellSize,
                    _warehousePadding);
                float renderedCellSize = _warehouseCellSize * widthScale;
                float renderedPadding = _warehousePadding * widthScale;
                float renderedLineWidth = _gridLineWidth * widthScale;
                float renderedInset = _itemInset * widthScale;
                int contentRows = RecipeWarehouseLayout.ContentRows(
                    layout.Rows,
                    _warehouseTrailingRows,
                    viewportSize.y,
                    renderedCellSize,
                    renderedPadding);

                float designWidth = _warehousePadding * 2f
                    + layout.Columns * _warehouseCellSize;
                float contentWidth = viewportSize.x > 0.01f
                    ? viewportSize.x
                    : designWidth;
                float contentHeight = renderedPadding * 2f
                    + contentRows * renderedCellSize;
                _dishContainer.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    contentWidth);
                _dishContainer.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    contentHeight);
                _dishContainer.anchoredPosition = new Vector2(
                    0f,
                    _dishContainer.anchoredPosition.y);

                int count = Mathf.Min(
                    _layoutDishes.Count,
                    layout.Placements.Count);
                for (int i = 0; i < count; i++)
                {
                    RecipeWarehouseLayout.Placement placement =
                        layout.Placements[i];
                    RectTransform rect =
                        (RectTransform)_layoutDishes[i].transform;
                    ConfigureDishRect(rect);
                    rect.anchoredPosition = new Vector2(
                        renderedPadding
                            + placement.Position.x * renderedCellSize
                            + renderedInset * 0.5f,
                        -renderedPadding
                            - placement.Position.y * renderedCellSize
                            - renderedInset * 0.5f);
                    rect.sizeDelta = new Vector2(
                        Mathf.Max(
                            1f,
                            placement.Size.x * renderedCellSize - renderedInset),
                        Mathf.Max(
                            1f,
                            placement.Size.y * renderedCellSize - renderedInset));
                }

                _gridGraphic.Configure(
                    renderedCellSize,
                    renderedPadding,
                    renderedLineWidth,
                    _surfaceColor,
                    _gridColor);
                _gridGraphic.transform.SetAsFirstSibling();
                Canvas.ForceUpdateCanvases();
                _hasLayout = true;
                SetVerticalNormalizedPosition(previousNormalized);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private bool ValidateHierarchy()
        {
            bool valid = _dishContainer != null
                && _scrollRect != null
                && _scrollRect.viewport != null
                && _gridGraphic != null;
            if (!valid)
            {
                Debug.LogError(
                    $"{nameof(RecipeWarehouseView)} prefab hierarchy is incomplete.",
                    this);
                return false;
            }

            _scrollRect.content = _dishContainer;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            return true;
        }

        private void BuildLayoutInputs()
        {
            _layoutDishes.Clear();
            _layoutSizes.Clear();
            RecipeEditDishView[] dishes =
                _dishContainer.GetComponentsInChildren<RecipeEditDishView>(false);
            for (int i = 0; i < dishes.Length; i++)
            {
                RecipeEditDishView dish = dishes[i];
                if (dish == null)
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
                $"Warehouse width expanded from {configuredColumns} to "
                + $"{effectiveColumns} columns because at least one dish is "
                + "wider than the configured warehouse.",
                this);
        }

        private Vector2 ViewportSize()
        {
            RectTransform viewport = _scrollRect != null
                ? _scrollRect.viewport
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
    }
}
