using System;
using DG.Tweening;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 编辑菜谱态中的单个菜品卡。编辑模式支持拖拽；选择模式禁用拖拽并响应点击。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RecipeEditDishView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float DragAlpha = 0.86f;

        [SerializeField] private Button _button;
        [SerializeField] private DishShapePreview _shapePreview;

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Transform _originalParent;
        private Vector2 _originalAnchoredPosition;
        private int _originalSiblingIndex;
        private Canvas _dragCanvas;
        private Action<RecipeEditDishView> _onClick;
        private Action<RecipeEditDishView> _onBeginDrag;
        private Func<RecipeEditDishView, bool> _onDragCancelled;
        private Action<RecipeEditDishView> _onHoverEnter;
        private Action<RecipeEditDishView> _onHoverExit;
        private bool _dragEnabled = true;
        private bool _dragging;
        private bool _dropHandled;
        private bool _hovered;

        public int BookIndex { get; private set; }
        public int DishIndex { get; private set; }
        public DishDef DishDef { get; private set; }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            RectTransform rect = _rect != null ? _rect : transform as RectTransform;
            if (rect == null)
            {
                return false;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, cam);
        }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rect = (RectTransform)transform;
            EnsureButton();
            ResolveShapePreview();
        }

        public void Bind(
            string name,
            string shape,
            int bookIndex,
            int dishIndex,
            bool dragEnabled = true,
            Action<RecipeEditDishView> onClick = null,
            DishDef dishDef = null,
            Action<RecipeEditDishView> onBeginDrag = null,
            Func<RecipeEditDishView, bool> onDragCancelled = null,
            Action<RecipeEditDishView> onHoverEnter = null,
            Action<RecipeEditDishView> onHoverExit = null)
        {
            BookIndex = bookIndex;
            DishIndex = dishIndex;
            DishDef = dishDef;
            _dragEnabled = dragEnabled;
            _onClick = onClick;
            _onBeginDrag = onBeginDrag;
            _onDragCancelled = onDragCancelled;
            _onHoverEnter = onHoverEnter;
            _onHoverExit = onHoverExit;
            _dropHandled = false;
            _hovered = false;

            if (_shapePreview != null)
            {
                if (dishDef != null)
                {
                    _shapePreview.Bind(dishDef);
                }
                else
                {
                    _shapePreview.Hide();
                }
            }

            EnsureButton();
            if (_button != null)
            {
                _button.interactable = onClick != null;
            }
        }

        public void MarkDropHandled()
        {
            _dropHandled = true;
            _dragging = false;
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }
        }

        public void PrepareAsFloating()
        {
            DOTween.Kill(_rect);
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }

            transform.SetAsLastSibling();
        }

        public void SetInteractableAfterAnimation(bool blocksRaycasts)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = blocksRaycasts;
                _canvasGroup.alpha = 1f;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_dragEnabled)
            {
                return;
            }

            _dragging = true;
            _dropHandled = false;
            _originalParent = transform.parent;
            _originalAnchoredPosition = _rect.anchoredPosition;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _dragCanvas = GetComponentInParent<Canvas>();
            HideHover();
            transform.SetParent(_dragCanvas != null ? _dragCanvas.transform : transform.root, true);
            transform.SetAsLastSibling();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = DragAlpha;
            _onBeginDrag?.Invoke(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragEnabled || !_dragging)
            {
                return;
            }

            RectTransform parentRect = _rect.parent as RectTransform;
            if (parentRect != null
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parentRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector3 worldPoint))
            {
                _rect.position = worldPoint;
                return;
            }

            _rect.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragEnabled || !_dragging)
            {
                return;
            }

            _dragging = false;
            if (_dropHandled)
            {
                return;
            }

            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 1f;
            if (_onDragCancelled != null && _onDragCancelled.Invoke(this))
            {
                return;
            }

            _canvasGroup.blocksRaycasts = true;
            if (_originalParent != null)
            {
                transform.SetParent(_originalParent, true);
                transform.SetSiblingIndex(_originalSiblingIndex);
                _rect.anchoredPosition = _originalAnchoredPosition;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_dragging)
            {
                _onClick?.Invoke(this);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_dragging || _dropHandled)
            {
                return;
            }

            _hovered = true;
            _onHoverEnter?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideHover();
        }

        private void HideHover()
        {
            if (!_hovered)
            {
                return;
            }

            _hovered = false;
            _onHoverExit?.Invoke(this);
        }

        private void EnsureButton()
        {
            if (_button != null)
            {
                return;
            }

            _button = GetComponent<Button>();
            if (_button == null)
            {
                return;
            }

            if (_button.targetGraphic == null)
            {
                _button.targetGraphic = GetComponentInChildren<Graphic>(true);
            }
        }

        private void ResolveShapePreview()
        {
            _shapePreview ??= GetComponentInChildren<DishShapePreview>(true);
        }
    }
}
