using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 圈孔（光圈收缩）转场的自定义 Graphic：绘制一个带圆形镂空的全屏遮罩环。
    /// 必须独立成文件（与类同名），否则作为组件烘进 prefab 时会被 Unity 序列化为「missing script」。
    /// 由 CartoonSceneTransitionForm 在 IrisWipe 转场时逐帧设置 <see cref="Radius"/> 驱动。
    /// </summary>
    public sealed class IrisWipeGraphic : MaskableGraphic
    {
        public float Radius { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            Vector2 center = rect.center;
            float outerRadius = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height) * 0.56f + 32f;
            float innerRadius = Mathf.Clamp(Radius, 0f, outerRadius);
            const int segments = 96;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 dir0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dir1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                int index = vh.currentVertCount;
                AddVert(vh, center + dir0 * outerRadius);
                AddVert(vh, center + dir1 * outerRadius);
                AddVert(vh, center + dir1 * innerRadius);
                AddVert(vh, center + dir0 * innerRadius);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index + 2, index + 3, index);
            }
        }

        private void AddVert(VertexHelper vh, Vector2 position)
        {
            UIVertex vert = UIVertex.simpleVert;
            vert.color = color;
            vert.position = position;
            vh.AddVert(vert);
        }
    }
}
