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
        private Color _fillColor;
        private Color _borderColor;
        private float _borderWidth = 2f;

        public void Configure(bool highlighted, bool clickable)
        {
            _fillColor = highlighted
                ? new Color(0.96f, 0.72f, 0.24f, 0.12f)
                : new Color(0.8f, 0.68f, 0.42f, 0.025f);
            float borderAlpha = highlighted ? 0.95f : (clickable ? 0.42f : 0.2f);
            _borderColor = new Color(0.96f, 0.72f, 0.24f, borderAlpha);
            _borderWidth = highlighted ? 3f : 2f;
            raycastTarget = false;
            SetVerticesDirty();
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
