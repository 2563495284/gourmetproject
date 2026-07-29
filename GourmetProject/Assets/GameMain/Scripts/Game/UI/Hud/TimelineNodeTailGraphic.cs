using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 节点气泡尾巴：以气泡底边为底、日期圆点为尖端绘制三角形。
    /// </summary>
    public sealed class TimelineNodeTailGraphic : MaskableGraphic
    {
        [SerializeField, Min(1f)] private float _baseWidth = 11f;

        private Vector2 _tip;

        public Vector2 Tip => _tip;

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
            float baseY = rect.yMin + 1f;
            Color32 vertexColor = color;

            var vertex = UIVertex.simpleVert;
            vertex.color = vertexColor;
            vertex.position = new Vector3(-half, baseY);
            vh.AddVert(vertex);
            vertex.position = new Vector3(half, baseY);
            vh.AddVert(vertex);
            vertex.position = _tip;
            vh.AddVert(vertex);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
