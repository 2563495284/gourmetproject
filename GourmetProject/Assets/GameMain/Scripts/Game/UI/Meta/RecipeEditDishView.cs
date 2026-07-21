using System;
using System.Collections.Generic;
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
        private static readonly Vector2 FloatingAnchor = new(0.5f, 0.5f);

        [SerializeField] private Button _button;
        [SerializeField] private DishShapePreview _shapePreview;

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Transform _originalParent;
        private Vector2 _originalAnchorMin;
        private Vector2 _originalAnchorMax;
        private Vector2 _originalPivot;
        private Vector2 _originalSizeDelta;
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
            Action<RecipeEditDishView> onHoverExit = null,
            IReadOnlyList<string> flavorIds = null)
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
                    _shapePreview.Bind(dishDef, flavorIds: flavorIds);
                }
                else
                {
                    _shapePreview.Hide();
                }
            }

            EnsureButton();
            if (_button != null)
            {
                _button.interactable = dragEnabled || onClick != null;
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
            Vector3 center = _rect.TransformPoint(_rect.rect.center);
            PrepareAsFloating(center);
        }

        public void SetInteractableAfterAnimation(bool blocksRaycasts)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = blocksRaycasts;
                _canvasGroup.alpha = 1f;
            }
        }

        public void PlayFlavorTransform(DishDef dishDef, IReadOnlyList<string> flavorIds, Action onComplete)
        {
            HideHover();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }

            if (_shapePreview == null)
            {
                onComplete?.Invoke();
                return;
            }

            _shapePreview.PlayTransformTo(dishDef, flavorIds, () =>
            {
                if (_canvasGroup != null)
                {
                    _canvasGroup.blocksRaycasts = true;
                    _canvasGroup.alpha = 1f;
                }

                onComplete?.Invoke();
            });
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
            _originalAnchorMin = _rect.anchorMin;
            _originalAnchorMax = _rect.anchorMax;
            _originalPivot = _rect.pivot;
            _originalSizeDelta = _rect.sizeDelta;
            _originalAnchoredPosition = _rect.anchoredPosition;
            _originalSiblingIndex = transform.GetSiblingIndex();
            _dragCanvas = GetComponentInParent<Canvas>();
            Vector3 center = _rect.TransformPoint(_rect.rect.center);
            HideHover();
            transform.SetParent(_dragCanvas != null ? _dragCanvas.transform : transform.root, true);
            PrepareAsFloating(center);
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 1f;
            MoveToPointer(eventData);
            _onBeginDrag?.Invoke(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragEnabled || !_dragging)
            {
                return;
            }

            MoveToPointer(eventData);
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
                _rect.anchorMin = _originalAnchorMin;
                _rect.anchorMax = _originalAnchorMax;
                _rect.pivot = _originalPivot;
                _rect.sizeDelta = _originalSizeDelta;
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

        private void PrepareAsFloating(Vector3 worldCenter)
        {
            DOTween.Kill(_rect);
            _rect.anchorMin = FloatingAnchor;
            _rect.anchorMax = FloatingAnchor;
            _rect.pivot = FloatingAnchor;
            _rect.sizeDelta = _originalSizeDelta.sqrMagnitude > 0.0001f ? _originalSizeDelta : _rect.sizeDelta;
            _rect.position = worldCenter;
            transform.SetAsLastSibling();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.alpha = 1f;
            }
        }

        private void MoveToPointer(PointerEventData eventData)
        {
            RectTransform parentRect = _rect.parent as RectTransform;
            Camera camera = _dragCanvas != null && _dragCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _dragCanvas.worldCamera
                : eventData.pressEventCamera;
            if (parentRect != null
                && RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parentRect,
                    eventData.position,
                    camera,
                    out Vector3 worldPoint))
            {
                _rect.position = worldPoint;
                return;
            }

            _rect.position = eventData.position;
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
