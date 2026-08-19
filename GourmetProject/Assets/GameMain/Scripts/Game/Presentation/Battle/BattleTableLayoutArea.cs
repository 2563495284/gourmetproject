using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// Battle 场景中的餐桌布局区域。设计师可用 Rect Tool 直接移动和缩放，
    /// 运行时会把当前胃的实际格子完整适配到这个世界空间矩形内。
    /// 结算框用独立的位置/尺寸字段，点击结算时从休息框 tween 过去。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class BattleTableLayoutArea : MonoBehaviour
    {
        [SerializeField] private Color _gizmoColor = new Color(0.2f, 0.9f, 0.75f, 0.85f);
        [SerializeField] private Color _settlementGizmoColor = new Color(1f, 0.72f, 0.2f, 0.7f);
        [Tooltip("结算时 TableLayoutArea 的锚点位置。默认向下扩进 BottomUI 腾出的空间。")]
        [SerializeField] private Vector2 _settlementAnchoredPosition = new Vector2(0f, 0.2f);
        [Tooltip("结算时 TableLayoutArea 的尺寸。默认加高、左右不动。")]
        [SerializeField] private Vector2 _settlementSizeDelta = new Vector2(11.3f, 9.2f);
        private const float DefaultSettlementExtraHeight = 2f;

        private Vector2 _restAnchoredPosition;
        private Vector2 _restSizeDelta;
        private bool _restCaptured;

        public Vector2 RestAnchoredPosition
        {
            get
            {
                CaptureRestIfNeeded();
                return _restAnchoredPosition;
            }
        }

        public Vector2 RestSizeDelta
        {
            get
            {
                CaptureRestIfNeeded();
                return _restSizeDelta;
            }
        }

        public Vector2 SettlementAnchoredPosition
        {
            get
            {
                if (HasSerializedSettlementLayout)
                {
                    return _settlementAnchoredPosition;
                }

                CaptureRestIfNeeded();
                return new Vector2(_restAnchoredPosition.x, _restAnchoredPosition.y - DefaultSettlementExtraHeight * 0.5f);
            }
        }

        public Vector2 SettlementSizeDelta
        {
            get
            {
                if (HasSerializedSettlementLayout)
                {
                    return _settlementSizeDelta;
                }

                CaptureRestIfNeeded();
                return new Vector2(_restSizeDelta.x, _restSizeDelta.y + DefaultSettlementExtraHeight);
            }
        }

        public bool HasSettlementLayout
        {
            get
            {
                Vector2 size = SettlementSizeDelta;
                return size.x > 0.01f && size.y > 0.01f;
            }
        }

        private bool HasSerializedSettlementLayout
            => _settlementSizeDelta.x > 0.01f && _settlementSizeDelta.y > 0.01f;

        private void Awake()
        {
            if (Application.isPlaying)
            {
                CaptureRestIfNeeded();
            }
        }

        public void ApplyRestLayout()
        {
            CaptureRestIfNeeded();
            ApplyLayout(_restAnchoredPosition, _restSizeDelta);
        }

        public void ApplySettlementLayout()
        {
            if (!HasSettlementLayout)
            {
                return;
            }

            CaptureRestIfNeeded();
            ApplyLayout(SettlementAnchoredPosition, SettlementSizeDelta);
        }

        public bool TryGetLayoutWorldBounds(
            bool settlement,
            out float left,
            out float right,
            out float bottom,
            out float top)
        {
            CaptureRestIfNeeded();
            var rect = (RectTransform)transform;
            Vector2 currentPosition = rect.anchoredPosition;
            Vector2 currentSize = rect.sizeDelta;
            Vector2 targetPosition = settlement ? SettlementAnchoredPosition : RestAnchoredPosition;
            Vector2 targetSize = settlement ? SettlementSizeDelta : RestSizeDelta;
            ApplyLayout(targetPosition, targetSize);
            bool ok = TryReadWorldBounds(rect, out left, out right, out bottom, out top);
            ApplyLayout(currentPosition, currentSize);
            return ok;
        }

        private void CaptureRestIfNeeded()
        {
            if (_restCaptured)
            {
                return;
            }

            var rect = (RectTransform)transform;
            _restAnchoredPosition = rect.anchoredPosition;
            _restSizeDelta = rect.sizeDelta;
            _restCaptured = true;
        }

        private void ApplyLayout(Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var rect = (RectTransform)transform;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static bool TryReadWorldBounds(
            RectTransform rect,
            out float left,
            out float right,
            out float bottom,
            out float top)
        {
            left = right = bottom = top = 0f;
            if (rect == null)
            {
                return false;
            }

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            left = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            right = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            bottom = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            top = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            return right > left && top > bottom;
        }

        private void OnDrawGizmos()
        {
            var rect = (RectTransform)transform;
            DrawWorldRect(rect, _gizmoColor);
            if (HasSettlementLayout)
            {
                DrawLocalRect(SettlementAnchoredPosition, SettlementSizeDelta, _settlementGizmoColor);
            }
        }

        private static void DrawWorldRect(RectTransform rect, Color color)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            DrawCorners(corners, color);
            DrawCross(rect.position, color);
        }

        private void DrawLocalRect(Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
        {
            Transform parent = transform.parent;
            Vector3 center = parent != null
                ? parent.TransformPoint(new Vector3(anchoredPosition.x, anchoredPosition.y, transform.localPosition.z))
                : new Vector3(anchoredPosition.x, anchoredPosition.y, transform.position.z);
            Vector3 lossy = transform.lossyScale;
            float halfW = sizeDelta.x * Mathf.Abs(lossy.x) * 0.5f;
            float halfH = sizeDelta.y * Mathf.Abs(lossy.y) * 0.5f;
            var corners = new[]
            {
                new Vector3(center.x - halfW, center.y - halfH, center.z),
                new Vector3(center.x - halfW, center.y + halfH, center.z),
                new Vector3(center.x + halfW, center.y + halfH, center.z),
                new Vector3(center.x + halfW, center.y - halfH, center.z),
            };
            DrawCorners(corners, color);
            DrawCross(center, color);
        }

        private static void DrawCorners(Vector3[] corners, Color color)
        {
            Color previous = Gizmos.color;
            Gizmos.color = color;
            for (int i = 0; i < corners.Length; i++)
            {
                Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Length]);
            }

            Gizmos.color = previous;
        }

        private static void DrawCross(Vector3 position, Color color)
        {
            Color previous = Gizmos.color;
            Gizmos.color = color;
            Gizmos.DrawLine(
                new Vector3(position.x - 0.12f, position.y, position.z),
                new Vector3(position.x + 0.12f, position.y, position.z));
            Gizmos.DrawLine(
                new Vector3(position.x, position.y - 0.12f, position.z),
                new Vector3(position.x, position.y + 0.12f, position.z));
            Gizmos.color = previous;
        }
    }
}
