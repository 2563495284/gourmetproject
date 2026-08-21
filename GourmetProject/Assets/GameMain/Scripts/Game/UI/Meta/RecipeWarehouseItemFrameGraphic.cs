using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 仓库食物的轻量背景与边框。与 Unity Outline 不同，只绘制真正的四条边。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecipeWarehouseItemFrameGraphic : MaskableGraphic
    {
        private static readonly Color NormalFill =
            new Color(0.9569f, 0.9216f, 0.8667f, 0.035f);
        private static readonly Color HighlightedFill =
            new Color(0.6627f, 0.7255f, 0.6314f, 0.18f);
        private static readonly Color NormalBorder =
            new Color(0.3961f, 0.4588f, 0.4f, 1f);
        private static readonly Color CannotPlaceFill =
            new Color(0.92f, 0.18f, 0.16f, 0.10f);
        private static readonly Color CannotPlaceBorder =
            new Color(0.96f, 0.20f, 0.18f, 1f);

        private Color _fillColor;
        private Color _borderColor;
        private float _borderWidth = 2f;

        internal Color FillColor => _fillColor;

        internal Color BorderColor => _borderColor;

        public void Configure(
            bool highlighted,
            bool clickable,
            bool cannotPlace = false)
        {
            if (cannotPlace)
            {
                _fillColor = CannotPlaceFill;
                _borderColor = WithAlpha(
                    CannotPlaceBorder,
                    highlighted ? 1f : 0.92f);
            }
            else
            {
                _fillColor = highlighted ? HighlightedFill : NormalFill;
                float borderAlpha = highlighted
                    ? 0.95f
                    : (clickable ? 0.42f : 0.2f);
                _borderColor = WithAlpha(NormalBorder, borderAlpha);
            }

            _borderWidth = highlighted ? 3f : 2f;
            raycastTarget = false;
            SetVerticesDirty();
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            AddQuad(vh, rect, _fillColor);

            float width = Mathf.Clamp(_borderWidth, 0.5f, Mathf.Min(rect.width, rect.height) * 0.5f);
            AddQuad(vh, Rect.MinMaxRect(rect.xMin, rect.yMin, rect.xMax, rect.yMin + width), _borderColor);
            AddQuad(vh, Rect.MinMaxRect(rect.xMin, rect.yMax - width, rect.xMax, rect.yMax), _borderColor);
            AddQuad(vh, Rect.MinMaxRect(rect.xMin, rect.yMin + width, rect.xMin + width, rect.yMax - width), _borderColor);
            AddQuad(vh, Rect.MinMaxRect(rect.xMax - width, rect.yMin + width, rect.xMax, rect.yMax - width), _borderColor);
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
