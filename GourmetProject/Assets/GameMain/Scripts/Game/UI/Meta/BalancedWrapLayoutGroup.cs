using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 按最多 N 列把子物体排成均衡网格：3 个一行、4 个 2x2、5 个 3x2。
    /// </summary>
    [AddComponentMenu("Layout/Balanced Wrap Layout Group")]
    public sealed class BalancedWrapLayoutGroup : LayoutGroup
    {
        [SerializeField, Min(1)] private int _maxColumns = 3;
        [SerializeField] private float _spacing = 24f;
        [SerializeField, Min(0f)] private float _maxChildSize = 400f;

        public int maxColumns
        {
            get => _maxColumns;
            set => SetProperty(ref _maxColumns, Mathf.Max(1, value));
        }

        public float spacing
        {
            get => _spacing;
            set => SetProperty(ref _spacing, value);
        }

        public float maxChildSize
        {
            get => _maxChildSize;
            set => SetProperty(ref _maxChildSize, Mathf.Max(0f, value));
        }

        public static int ResolveColumnCount(int itemCount, int maxColumns = 3)
        {
            if (itemCount <= 0)
            {
                return 1;
            }

            int cappedMax = Mathf.Max(1, maxColumns);
            if (itemCount <= cappedMax)
            {
                return itemCount;
            }

            int rows = Mathf.CeilToInt(itemCount / (float)cappedMax);
            return Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)rows));
        }

        public static int ResolveRowCount(int itemCount, int columnCount)
        {
            if (itemCount <= 0 || columnCount <= 0)
            {
                return 1;
            }

            return Mathf.CeilToInt(itemCount / (float)columnCount);
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            int count = rectChildren.Count;
            int columns = ResolveColumnCount(count, _maxColumns);
            float cell = ResolveCellSize(columns, ResolveRowCount(count, columns));
            float width = padding.horizontal
                + cell * columns
                + _spacing * Mathf.Max(0, columns - 1);
            SetLayoutInputForAxis(width, width, -1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            int count = rectChildren.Count;
            int columns = ResolveColumnCount(count, _maxColumns);
            int rows = ResolveRowCount(count, columns);
            float cell = ResolveCellSize(columns, rows);
            float height = padding.vertical
                + cell * rows
                + _spacing * Mathf.Max(0, rows - 1);
            SetLayoutInputForAxis(height, height, -1f, 1);
        }

        public override void SetLayoutHorizontal()
        {
            ApplyLayout(0);
        }

        public override void SetLayoutVertical()
        {
            ApplyLayout(1);
        }

        private void ApplyLayout(int axis)
        {
            int count = rectChildren.Count;
            if (count == 0)
            {
                return;
            }

            int columns = ResolveColumnCount(count, _maxColumns);
            int rows = ResolveRowCount(count, columns);
            float cell = ResolveCellSize(columns, rows);
            float available = rectTransform.rect.size[axis]
                - (axis == 0 ? padding.horizontal : padding.vertical);
            int cells = axis == 0 ? columns : rows;
            float gridSize = cell * cells + _spacing * Mathf.Max(0, cells - 1);
            float start = (axis == 0 ? padding.left : padding.top)
                + (available - gridSize) * GetAlignmentOnAxis(axis);

            for (int i = 0; i < count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                int index = axis == 0 ? column : row;
                float position = start + index * (cell + _spacing);
                SetChildAlongAxis(rectChildren[i], axis, position, cell);
            }
        }

        private float ResolveCellSize(int columns, int rows)
        {
            float availableWidth = Mathf.Max(0f, rectTransform.rect.width - padding.horizontal);
            float availableHeight = Mathf.Max(0f, rectTransform.rect.height - padding.vertical);
            float widthPerChild = columns <= 0
                ? 0f
                : Mathf.Max(0f, availableWidth - _spacing * Mathf.Max(0, columns - 1)) / columns;
            float heightPerChild = rows <= 0
                ? 0f
                : Mathf.Max(0f, availableHeight - _spacing * Mathf.Max(0, rows - 1)) / rows;
            float fitted = Mathf.Min(widthPerChild, heightPerChild);
            return _maxChildSize > 0f ? Mathf.Min(fitted, _maxChildSize) : fitted;
        }
    }
}
