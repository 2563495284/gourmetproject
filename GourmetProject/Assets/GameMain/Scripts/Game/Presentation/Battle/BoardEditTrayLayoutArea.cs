using System.Collections.Generic;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 餐桌编辑页候选碎片的可视布局区域。设计师可用 Rect Tool 移动和缩放，
    /// 运行时会按候选数量等分横向列，并用统一格子尺寸把所有候选完整放进区域。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BoardEditTrayLayoutArea : MonoBehaviour
    {
        [SerializeField] private Vector2 _padding = new Vector2(0.25f, 0.25f);
        [SerializeField, Min(0f)] private float _columnGap = 0.25f;
        [SerializeField, Min(0.01f)] private float _maxCellSize = 0.6f;
        [SerializeField, Min(0f)] private float _hitPadding = 0.15f;
        [SerializeField, Min(1)] private int _previewColumns = 3;
        [SerializeField] private Color _gizmoColor = new Color(1f, 0.72f, 0.18f, 0.9f);

        public RectTransform RectTransform => (RectTransform)transform;
        public float HitPadding => Mathf.Max(0f, _hitPadding);

        public bool TryCalculate(
            IReadOnlyList<Vector2Int> fragmentSizes,
            List<Vector2> columnCenters,
            out float cellSize)
        {
            return TryCalculate(
                RectTransform.rect,
                fragmentSizes,
                _padding,
                _columnGap,
                _maxCellSize,
                columnCenters,
                out cellSize);
        }

        /// <summary>
        /// 纯布局计算：按候选数量等分区域，用候选中的最大宽高求统一格子尺寸。
        /// </summary>
        public static bool TryCalculate(
            Rect rect,
            IReadOnlyList<Vector2Int> fragmentSizes,
            Vector2 padding,
            float columnGap,
            float maxCellSize,
            List<Vector2> columnCenters,
            out float cellSize)
        {
            cellSize = 0f;
            columnCenters?.Clear();
            if (fragmentSizes == null || fragmentSizes.Count == 0 || columnCenters == null)
            {
                return false;
            }

            int maxWidth = 0;
            int maxHeight = 0;
            for (int i = 0; i < fragmentSizes.Count; i++)
            {
                Vector2Int size = fragmentSizes[i];
                maxWidth = Mathf.Max(maxWidth, size.x);
                maxHeight = Mathf.Max(maxHeight, size.y);
            }

            if (maxWidth <= 0 || maxHeight <= 0)
            {
                return false;
            }

            int count = fragmentSizes.Count;
            float safeGap = Mathf.Max(0f, columnGap);
            float innerWidth = rect.width - Mathf.Max(0f, padding.x) * 2f - safeGap * Mathf.Max(0, count - 1);
            float innerHeight = rect.height - Mathf.Max(0f, padding.y) * 2f;
            if (innerWidth <= 0f || innerHeight <= 0f)
            {
                return false;
            }

            float columnWidth = innerWidth / count;
            cellSize = Mathf.Min(
                Mathf.Max(0.01f, maxCellSize),
                columnWidth / maxWidth,
                innerHeight / maxHeight);
            if (cellSize <= 0.001f)
            {
                cellSize = 0f;
                return false;
            }

            float firstCenterX = rect.xMin + Mathf.Max(0f, padding.x) + columnWidth * 0.5f;
            for (int i = 0; i < count; i++)
            {
                columnCenters.Add(new Vector2(
                    firstCenterX + i * (columnWidth + safeGap),
                    rect.center.y));
            }

            return true;
        }

        private void OnValidate()
        {
            _padding.x = Mathf.Max(0f, _padding.x);
            _padding.y = Mathf.Max(0f, _padding.y);
            _columnGap = Mathf.Max(0f, _columnGap);
            _maxCellSize = Mathf.Max(0.01f, _maxCellSize);
            _hitPadding = Mathf.Max(0f, _hitPadding);
            _previewColumns = Mathf.Max(1, _previewColumns);
        }

        private void OnDrawGizmos()
        {
            RectTransform rectTransform = RectTransform;
            Rect rect = rectTransform.rect;
            Color previous = Gizmos.color;
            Gizmos.color = _gizmoColor;

            Vector3 bottomLeft = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
            Vector3 topLeft = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
            Vector3 topRight = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f));
            Vector3 bottomRight = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f));
            Gizmos.DrawLine(bottomLeft, topLeft);
            Gizmos.DrawLine(topLeft, topRight);
            Gizmos.DrawLine(topRight, bottomRight);
            Gizmos.DrawLine(bottomRight, bottomLeft);

            int columns = Mathf.Max(1, _previewColumns);
            for (int i = 1; i < columns; i++)
            {
                float t = i / (float)columns;
                float x = Mathf.Lerp(rect.xMin, rect.xMax, t);
                Gizmos.DrawLine(
                    rectTransform.TransformPoint(new Vector3(x, rect.yMin, 0f)),
                    rectTransform.TransformPoint(new Vector3(x, rect.yMax, 0f)));
            }

            Gizmos.color = previous;
        }
    }
}
