using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 用单个 Graphic 绘制仓库底色与网格线，避免为每个格子实例化 Image。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecipeWarehouseGridGraphic : MaskableGraphic
    {
        [SerializeField] private Color _backgroundColor = new(0.075f, 0.09f, 0.095f, 1f);
        [SerializeField] private Color _gridColor = new(0.34f, 0.39f, 0.4f, 0.82f);
        [SerializeField] private float _cellSize = 88f;
        [SerializeField] private float _padding = 24f;
        [SerializeField] private float _lineWidth = 2f;

        public void Configure(
            float cellSize,
            float padding,
            float lineWidth,
            Color backgroundColor,
            Color gridColor)
        {
            _cellSize = Mathf.Max(1f, cellSize);
            _padding = Mathf.Max(0f, padding);
            _lineWidth = Mathf.Max(0.5f, lineWidth);
            _backgroundColor = backgroundColor;
            _gridColor = gridColor;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            AddQuad(vh, rect, _backgroundColor);

            float cell = Mathf.Max(1f, _cellSize);
            float halfLine = Mathf.Max(0.25f, _lineWidth * 0.5f);
            float left = rect.xMin + _padding;
            float right = rect.xMax - _padding;
            float bottom = rect.yMin + _padding;
            float top = rect.yMax - _padding;
            if (right <= left || top <= bottom)
            {
                return;
            }

            int verticalLines = Mathf.Min(512, Mathf.FloorToInt((right - left) / cell) + 1);
            int horizontalLines = Mathf.Min(512, Mathf.FloorToInt((top - bottom) / cell) + 1);
            for (int i = 0; i < verticalLines; i++)
            {
                float x = left + i * cell;
                AddQuad(
                    vh,
                    Rect.MinMaxRect(x - halfLine, bottom, x + halfLine, top),
                    _gridColor);
            }

            for (int i = 0; i < horizontalLines; i++)
            {
                float y = top - i * cell;
                AddQuad(
                    vh,
                    Rect.MinMaxRect(left, y - halfLine, right, y + halfLine),
                    _gridColor);
            }
        }

        private static void AddQuad(VertexHelper vh, Rect rect, Color color)
        {
            int start = vh.currentVertCount;
            AddVertex(vh, new Vector2(rect.xMin, rect.yMin), color);
            AddVertex(vh, new Vector2(rect.xMin, rect.yMax), color);
            AddVertex(vh, new Vector2(rect.xMax, rect.yMax), color);
            AddVertex(vh, new Vector2(rect.xMax, rect.yMin), color);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private static void AddVertex(VertexHelper vh, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vh.AddVert(vertex);
        }
    }
}
