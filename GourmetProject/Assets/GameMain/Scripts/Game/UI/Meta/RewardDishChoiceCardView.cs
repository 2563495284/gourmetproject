using System;
using DG.Tweening;
using GourmetProject.Game.Meta;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 菜品包领奖页上方的一张候选菜品卡。只负责展示与拖拽起止事件，落点结算由 RewardDishPackPanel 处理。
    /// </summary>
    public sealed class RewardDishChoiceCardView : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Image _icon;
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private CanvasGroup _canvasGroup;

        private RectTransform _rect;
        private RectTransform _iconRect;
        private Canvas _canvas;
        private Transform _originalParent;
        private Vector2 _originalAnchoredPosition;
        private Canvas _dragCanvas;
        private int _choiceIndex = -1;
        private bool _resolved;
        private Action<RewardDishChoiceCardView, int> _onPointerDown;
        private Action<RewardDishChoiceCardView, int, Vector2> _onPointerUp;
        private Tween _failureTween;

        public RectTransform Rect
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

        public int ChoiceIndex => _choiceIndex;

        public void Bind(
            RewardChoice choice,
            Sprite icon,
            int choiceIndex,
            Action<RewardDishChoiceCardView, int> onPointerDown,
            Action<RewardDishChoiceCardView, int, Vector2> onPointerUp)
        {
            EnsureRefs();
            _choiceIndex = choiceIndex;
            _resolved = false;
            _onPointerDown = onPointerDown;
            _onPointerUp = onPointerUp;

            if (_nameText != null)
            {
                _nameText.text = choice?.Name ?? string.Empty;
            }

            if (_descriptionText != null)
            {
                _descriptionText.text = choice?.Description ?? string.Empty;
            }

            SetIcon(icon);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
        }

        public void SetResolved(bool resolved)
        {
            EnsureRefs();
            _resolved = resolved;
            if (_button != null)
            {
                _button.interactable = !resolved;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = resolved ? 0.45f : 1f;
                _canvasGroup.interactable = !resolved;
                _canvasGroup.blocksRaycasts = !resolved;
            }
        }

        public Vector2 IconScreenCenter()
        {
            RectTransform target = _iconRect != null ? _iconRect : Rect;
            Camera cam = ResolveEventCamera();
            return RectTransformUtility.WorldToScreenPoint(cam, target.TransformPoint(target.rect.center));
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            Camera cam = ResolveEventCamera();
            return RectTransformUtility.RectangleContainsScreenPoint(Rect, screenPoint, cam);
        }

        public void PlayTargetFailed()
        {
            RectTransform target = _iconRect != null ? _iconRect : Rect;
            if (target == null)
            {
                return;
            }

            _failureTween?.Kill();
            Vector2 origin = target.anchoredPosition;
            _failureTween = DOVirtual.Float(0f, 1f, 0.25f, t =>
            {
                if (target == null)
                {
                    return;
                }

                float offset = Mathf.Sin(t * Mathf.PI * 12f) * 9f * (1f - t);
                target.anchoredPosition = origin + new Vector2(offset, 0f);
            })
                .SetEase(Ease.Linear)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    if (target != null)
                    {
                        target.anchoredPosition = origin;
                    }
                });
        }

        private void OnDestroy()
        {
            _failureTween?.Kill();
        }

        private void HandlePointerDown(PointerEventData eventData)
        {
            if (_resolved)
            {
                return;
            }

            _onPointerDown?.Invoke(this, _choiceIndex);
        }

        private void HandlePointerUp(PointerEventData eventData)
        {
            if (_resolved)
            {
                return;
            }

            _onPointerUp?.Invoke(this, _choiceIndex, eventData.position);
        }

        private void HandleBeginDrag(PointerEventData eventData)
        {
            if (_resolved)
            {
                return;
            }

            EnsureRefs();
            _originalParent = transform.parent;
            _originalAnchoredPosition = Rect.anchoredPosition;
            _dragCanvas = GetComponentInParent<Canvas>();
            transform.SetParent(_dragCanvas != null ? _dragCanvas.transform : transform.root, true);
            transform.SetAsLastSibling();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.82f;
        }

        private void HandleDrag(PointerEventData eventData)
        {
            if (_resolved)
            {
                return;
            }

            RectTransform parentRect = Rect.parent as RectTransform;
            if (parentRect != null
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parentRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector3 worldPoint))
            {
                Rect.position = worldPoint;
                return;
            }

            Rect.position = eventData.position;
        }

        private void HandleEndDrag(PointerEventData eventData)
        {
            EnsureRefs();
            _canvasGroup.blocksRaycasts = !_resolved;
            _canvasGroup.alpha = _resolved ? 0.45f : 1f;
            if (_originalParent != null)
            {
                transform.SetParent(_originalParent, true);
                Rect.anchoredPosition = _originalAnchoredPosition;
            }
        }

        private void EnsureRefs()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>() ?? GetComponentInChildren<Button>(true);
            }

            if (_icon == null)
            {
                Transform icon = transform.Find("IconFrame/Icon") ?? transform.Find("Icon");
                _icon = icon != null ? icon.GetComponent<Image>() : null;
            }

            if (_nameText == null)
            {
                Transform title = transform.Find("Texts/Title") ?? transform.Find("Title") ?? transform.Find("Name");
                _nameText = title != null ? title.GetComponent<Text>() : null;
            }

            if (_descriptionText == null)
            {
                Transform desc = transform.Find("Texts/Description") ?? transform.Find("Description");
                _descriptionText = desc != null ? desc.GetComponent<Text>() : null;
            }

            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            EnsurePointerProxy();
        }

        private void SetIcon(Sprite icon)
        {
            if (_icon == null)
            {
                return;
            }

            _iconRect = _icon.rectTransform;
            _icon.preserveAspect = true;
            _icon.enabled = icon != null;
            _icon.sprite = icon;
            _icon.color = Color.white;
        }

        private Camera ResolveEventCamera()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            return _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;
        }

        private void EnsurePointerProxy()
        {
            GameObject target = _button != null ? _button.gameObject : gameObject;
            PointerProxy proxy = target.GetComponent<PointerProxy>();
            if (proxy == null)
            {
                proxy = target.AddComponent<PointerProxy>();
            }

            proxy.Bind(this);
        }

        private sealed class PointerProxy : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private RewardDishChoiceCardView _owner;

            public void Bind(RewardDishChoiceCardView owner)
            {
                _owner = owner;
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                _owner?.HandlePointerDown(eventData);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _owner?.HandlePointerUp(eventData);
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                _owner?.HandleBeginDrag(eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                _owner?.HandleDrag(eventData);
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                _owner?.HandleEndDrag(eventData);
            }
        }
    }
}
