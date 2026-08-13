using UnityEngine;
using UnityEngine.Sprites;
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
        [SerializeField] private Sprite _cellSprite;
        [SerializeField] private Color _cellColor = Color.white;
        [SerializeField] private float _cellInset = 2f;

        public override Texture mainTexture =>
            _cellSprite != null && _cellSprite.texture != null
                ? _cellSprite.texture
                : base.mainTexture;

        public void Configure(
            float cellSize,
            float padding,
            float lineWidth,
            Color backgroundColor,
            Color gridColor)
        {
            Configure(
                cellSize,
                padding,
                lineWidth,
                backgroundColor,
                gridColor,
                null,
                Color.white,
                0f);
        }

        public void Configure(
            float cellSize,
            float padding,
            float lineWidth,
            Color backgroundColor,
            Color gridColor,
            Sprite cellSprite,
            Color cellColor,
            float cellInset)
        {
            _cellSize = Mathf.Max(1f, cellSize);
            _padding = Mathf.Max(0f, padding);
            _lineWidth = Mathf.Max(0.5f, lineWidth);
            _backgroundColor = backgroundColor;
            _gridColor = gridColor;
            _cellSprite = cellSprite;
            _cellColor = cellColor;
            _cellInset = Mathf.Max(0f, cellInset);
            raycastTarget = false;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;

            if (_cellSprite == null)
            {
                AddQuad(vh, rect, _backgroundColor);
            }

            float cell = Mathf.Max(1f, _cellSize);
            float left = rect.xMin + _padding;
            float right = rect.xMax - _padding;
            float bottom = rect.yMin + _padding;
            float top = rect.yMax - _padding;
            if (right <= left || top <= bottom)
            {
                return;
            }

            if (_cellSprite != null)
            {
                AddSpriteCells(vh, left, right, bottom, top, cell);
                return;
            }

            float halfLine = Mathf.Max(0.25f, _lineWidth * 0.5f);

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

        private void AddSpriteCells(
            VertexHelper vh,
            float left,
            float right,
            float bottom,
            float top,
            float cell)
        {
            int columns = Mathf.Min(
                512,
                Mathf.Max(1, Mathf.RoundToInt((right - left) / cell)));
            int rows = Mathf.Min(
                512,
                Mathf.Max(1, Mathf.CeilToInt((top - bottom) / cell)));
            float inset = Mathf.Min(_cellInset, cell * 0.25f);
            Vector4 outerUv = DataUtility.GetOuterUV(_cellSprite);
            Rect uv = Rect.MinMaxRect(
                outerUv.x,
                outerUv.y,
                outerUv.z,
                outerUv.w);

            for (int y = 0; y < rows; y++)
            {
                float yMax = top - y * cell - inset;
                float yMin = Mathf.Max(bottom, yMax - cell + inset * 2f);
                if (yMax <= yMin)
                {
                    continue;
                }

                for (int x = 0; x < columns; x++)
                {
                    float xMin = left + x * cell + inset;
                    float xMax = Mathf.Min(right, xMin + cell - inset * 2f);
                    if (xMax <= xMin)
                    {
                        continue;
                    }

                    AddQuad(
                        vh,
                        Rect.MinMaxRect(xMin, yMin, xMax, yMax),
                        _cellColor,
                        uv);
                }
            }
        }

        private static void AddQuad(VertexHelper vh, Rect rect, Color color)
        {
            AddQuad(vh, rect, color, new Rect(0f, 0f, 1f, 1f));
        }

        private static void AddQuad(
            VertexHelper vh,
            Rect rect,
            Color color,
            Rect uv)
        {
            int start = vh.currentVertCount;
            AddVertex(
                vh,
                new Vector2(rect.xMin, rect.yMin),
                color,
                new Vector2(uv.xMin, uv.yMin));
            AddVertex(
                vh,
                new Vector2(rect.xMin, rect.yMax),
                color,
                new Vector2(uv.xMin, uv.yMax));
            AddVertex(
                vh,
                new Vector2(rect.xMax, rect.yMax),
                color,
                new Vector2(uv.xMax, uv.yMax));
            AddVertex(
                vh,
                new Vector2(rect.xMax, rect.yMin),
                color,
                new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        private static void AddVertex(
            VertexHelper vh,
            Vector2 position,
            Color color,
            Vector2 uv)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = uv;
            vh.AddVert(vertex);
        }
    }
}
