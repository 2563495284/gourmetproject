using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Battle.View
{
    /// <summary>
    /// 食物调整态的全屏遮黑 + 餐桌高亮：用 4 条黑色半透明条围绕 BoardArea 拼出「中间挖洞」的遮罩
    /// （世界餐桌在 UI 之后，未遮住的洞即高亮透出），洞四周再加一圈亮色描边框做高亮。
    /// 自身挂 overrideSorting 的 Canvas，压在常驻 HUD 之上；食物调整按钮用更高 sortingOrder 浮于本遮罩之上。
    /// </summary>
    public sealed class FoodAdjustOverlay : MonoBehaviour
    {
        private const int OverlaySortingOrder = 500;
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color FrameColor = new Color(1f, 0.86f, 0.42f, 0.9f);
        private const float FrameThickness = 4f;

        private RectTransform _rect;
        private RectTransform _boardArea;

        [SerializeField] private Image _dimTop;
        [SerializeField] private Image _dimBottom;
        [SerializeField] private Image _dimLeft;
        [SerializeField] private Image _dimRight;
        [SerializeField] private Image _frameTop;
        [SerializeField] private Image _frameBottom;
        [SerializeField] private Image _frameLeft;
        [SerializeField] private Image _frameRight;
        
        private void Awake()
        {
            _rect = (RectTransform)transform;
            Stretch(_rect);
            ConfigureStrip(_dimTop, DimColor, true);
            ConfigureStrip(_dimBottom, DimColor, true);
            ConfigureStrip(_dimLeft, DimColor, true);
            ConfigureStrip(_dimRight, DimColor, true);
            ConfigureStrip(_frameTop, FrameColor, false);
            ConfigureStrip(_frameBottom, FrameColor, false);
            ConfigureStrip(_frameLeft, FrameColor, false);
            ConfigureStrip(_frameRight, FrameColor, false);

            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = OverlaySortingOrder;
            }

            if (_dimTop == null || _dimBottom == null || _dimLeft == null || _dimRight == null
                || _frameTop == null || _frameBottom == null || _frameLeft == null || _frameRight == null)
            {
                Debug.LogError($"{nameof(FoodAdjustOverlay)} prefab 缺少 4 条 Dim 和 4 条 Frame 图片。", this);
            }
        }

        private static void ConfigureStrip(Image image, Color color, bool blockRaycast)
        {
            if (image == null)
            {
                return;
            }

            image.color = color;
            image.raycastTarget = blockRaycast;
            var rect = (RectTransform)image.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        public void Show(RectTransform boardArea)
        {
            _boardArea = boardArea;
            gameObject.SetActive(true);
            Layout();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Layout()
        {
            if (_boardArea == null || _rect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            var corners = new Vector3[4];
            _boardArea.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, cam, out Vector2 local))
                {
                    minX = Mathf.Min(minX, local.x);
                    maxX = Mathf.Max(maxX, local.x);
                    minY = Mathf.Min(minY, local.y);
                    maxY = Mathf.Max(maxY, local.y);
                }
            }

            Rect r = _rect.rect;
            // 遮罩四条：围住洞外的整块屏幕。
            SetStrip(_dimTop, r.xMin, r.xMax, maxY, r.yMax);
            SetStrip(_dimBottom, r.xMin, r.xMax, r.yMin, minY);
            SetStrip(_dimLeft, r.xMin, minX, minY, maxY);
            SetStrip(_dimRight, maxX, r.xMax, minY, maxY);

            // 高亮描边框：紧贴洞的四边。
            SetStrip(_frameTop, minX, maxX, maxY - FrameThickness, maxY);
            SetStrip(_frameBottom, minX, maxX, minY, minY + FrameThickness);
            SetStrip(_frameLeft, minX, minX + FrameThickness, minY, maxY);
            SetStrip(_frameRight, maxX - FrameThickness, maxX, minY, maxY);
        }

        private static void SetStrip(Image strip, float xMin, float xMax, float yMin, float yMax)
        {
            if (strip == null)
            {
                return;
            }

            float w = Mathf.Max(0f, xMax - xMin);
            float h = Mathf.Max(0f, yMax - yMin);
            var rect = (RectTransform)strip.transform;
            rect.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            strip.enabled = w > 0.5f && h > 0.5f;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
    }
}
