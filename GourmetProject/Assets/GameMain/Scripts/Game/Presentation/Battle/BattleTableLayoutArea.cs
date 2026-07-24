using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// Battle 场景中的餐桌布局区域。设计师可用 Rect Tool 直接移动和缩放，
    /// 运行时会把当前胃的实际格子完整适配到这个世界空间矩形内。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BattleTableLayoutArea : MonoBehaviour
    {
        [SerializeField] private Color _gizmoColor = new Color(0.2f, 0.9f, 0.75f, 0.85f);

        private void OnDrawGizmos()
        {
            var rect = (RectTransform)transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            Color previous = Gizmos.color;
            Gizmos.color = _gizmoColor;
            for (int i = 0; i < corners.Length; i++)
            {
                Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Length]);
            }

            Gizmos.DrawLine(
                new Vector3(rect.position.x - 0.12f, rect.position.y, rect.position.z),
                new Vector3(rect.position.x + 0.12f, rect.position.y, rect.position.z));
            Gizmos.DrawLine(
                new Vector3(rect.position.x, rect.position.y - 0.12f, rect.position.z),
                new Vector3(rect.position.x, rect.position.y + 0.12f, rect.position.z));
            Gizmos.color = previous;
        }
    }
}
