using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 商店菜品购买时的目标箭头。根节点铺满 Canvas，起点固定在商品卡，终点跟随鼠标。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class TargetArrowView : MonoBehaviour
    {
        [SerializeField] private RectTransform _line;
        [SerializeField] private RectTransform _head;
        [SerializeField] private float _lineWidth = 8f;
        [SerializeField] private float _headGap = 18f;

        private RectTransform _rect;
        private Canvas _canvas;
        private Camera _eventCamera;
        private Vector2 _startScreenPoint;

        private RectTransform Rect
        {
            get
            {
                if (_rect == null)
                {
                    _rect = (RectTransform)transform;
                }

                return _rect;
            }
        }

        private void Awake()
        {
            EnsureRefs();
            BindCanvas();
        }

        private void Update()
        {
            if (Mouse.current == null)
            {
                return;
            }

            SetEndScreenPoint(Mouse.current.position.ReadValue());
        }

        public void SetupArrow(Vector2 startScreenPoint)
        {
            _startScreenPoint = startScreenPoint;
            BindCanvas();
            StretchToParent();
            SetEndScreenPoint(Mouse.current != null ? Mouse.current.position.ReadValue() : startScreenPoint);
        }

        public void SetEndScreenPoint(Vector2 endScreenPoint)
        {
            EnsureRefs();
            if (_line == null || _head == null
                || !TryScreenToLocal(_startScreenPoint, out Vector2 startLocal)
                || !TryScreenToLocal(endScreenPoint, out Vector2 endLocal))
            {
                return;
            }

            Vector2 delta = endLocal - startLocal;
            float distance = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Vector2 direction = distance > 0.001f ? delta / distance : Vector2.right;

            _line.pivot = new Vector2(0f, 0.5f);
            _line.anchorMin = new Vector2(0.5f, 0.5f);
            _line.anchorMax = new Vector2(0.5f, 0.5f);
            _line.anchoredPosition = startLocal;
            _line.sizeDelta = new Vector2(Mathf.Max(0f, distance - _headGap), _lineWidth);
            _line.localRotation = Quaternion.Euler(0f, 0f, angle);

            _head.anchorMin = new Vector2(0.5f, 0.5f);
            _head.anchorMax = new Vector2(0.5f, 0.5f);
            _head.anchoredPosition = endLocal - direction * (_headGap * 0.25f);
            _head.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private bool TryScreenToLocal(Vector2 screenPoint, out Vector2 localPoint)
        {
            BindCanvas();
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, _eventCamera, out localPoint);
        }

        private void BindCanvas()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            _eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
        }

        private void StretchToParent()
        {
            RectTransform rect = Rect;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void EnsureRefs()
        {
            if (_line == null)
            {
                Transform line = transform.Find("Line");
                _line = line as RectTransform;
            }

            if (_head == null)
            {
                Transform head = transform.Find("Head");
                _head = head as RectTransform;
            }

            if (_line == null)
            {
                _line = CreateImageChild("Line", Resources.Load<Sprite>("Sprites/UI/white"), new Vector2(80f, _lineWidth));
            }

            if (_head == null)
            {
                _head = CreateImageChild("Head", Resources.Load<Sprite>("Sprites/UI/ArrowHead"), new Vector2(42f, 42f));
            }
        }

        private RectTransform CreateImageChild(string childName, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = childName == "Head";
            return rect;
        }
    }
}
