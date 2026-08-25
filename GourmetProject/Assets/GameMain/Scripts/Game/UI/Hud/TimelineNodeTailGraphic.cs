using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 节点气泡尾巴：以气泡底边为底、日期圆点为尖端绘制三角形。
    /// </summary>
    public sealed class TimelineNodeTailGraphic : MaskableGraphic
    {
        [SerializeField, Min(1f)] private float _baseWidth = 16f;
        [SerializeField, Min(0f)] private float _baseOverlap = 4f;
        [SerializeField, Min(0f)] private float _outlineWidth = 2f;
        [SerializeField] private Color _outlineColor = new Color32(91, 57, 38, 255);

        private Vector2 _tip;

        public Vector2 Tip => _tip;
        public float BaseOverlap => _baseOverlap;

        public void SetBaseOverlap(float value)
        {
            value = Mathf.Max(0f, value);
            if (Mathf.Approximately(_baseOverlap, value))
            {
                return;
            }

            _baseOverlap = value;
            SetVerticesDirty();
        }

        public void SetTip(Vector2 localTip, Color value)
        {
            if ((_tip - localTip).sqrMagnitude < 0.0001f && color == value)
            {
                return;
            }

            _tip = localTip;
            color = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            float half = Mathf.Max(0.5f, _baseWidth * 0.5f);
            // 把尾巴底边压进气泡内部，避免缩放或像素取整时露出背景缝隙。
            float baseY = rect.yMin + _baseOverlap;
            float outline = Mathf.Clamp(_outlineWidth, 0f, half - 0.5f);

            if (outline <= 0.01f)
            {
                AddTriangle(vh, half, baseY, _tip, color);
                return;
            }

            AddTriangle(vh, half, baseY, _tip, _outlineColor);
            Vector2 direction = new Vector2(0f, baseY) - _tip;
            Vector2 innerTip = _tip + direction.normalized * outline;
            AddTriangle(
                vh,
                half - outline,
                baseY + outline,
                innerTip,
                color);
        }

        private static void AddTriangle(
            VertexHelper vh,
            float halfWidth,
            float baseY,
            Vector2 tip,
            Color32 value)
        {
            int start = vh.currentVertCount;

            var vertex = UIVertex.simpleVert;
            vertex.color = value;
            vertex.position = new Vector3(-halfWidth, baseY);
            vh.AddVert(vertex);
            vertex.position = new Vector3(halfWidth, baseY);
            vh.AddVert(vertex);
            vertex.position = tip;
            vh.AddVert(vertex);
            vh.AddTriangle(start, start + 1, start + 2);
        }
    }
}
