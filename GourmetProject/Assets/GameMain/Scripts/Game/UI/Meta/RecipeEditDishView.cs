using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 编辑菜谱态中的单个菜品卡。编辑模式支持拖拽；选择模式禁用拖拽并响应点击。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RecipeEditDishView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _shapeText;
        [SerializeField] private Button _button;

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Transform _originalParent;
        private Vector2 _originalAnchoredPosition;
        private Canvas _dragCanvas;
        private Action<RecipeEditDishView> _onClick;
        private bool _dragEnabled = true;
        private bool _dragging;

        public int BookIndex { get; private set; }
        public int DishIndex { get; private set; }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rect = (RectTransform)transform;
            EnsureButton();
        }

        public void Bind(
            string name,
            string shape,
            int bookIndex,
            int dishIndex,
            bool dragEnabled = true,
            Action<RecipeEditDishView> onClick = null)
        {
            BookIndex = bookIndex;
            DishIndex = dishIndex;
            _dragEnabled = dragEnabled;
            _onClick = onClick;

            if (_nameText != null)
            {
                _nameText.text = name ?? string.Empty;
            }

            if (_shapeText != null)
            {
                _shapeText.text = shape ?? string.Empty;
                _shapeText.gameObject.SetActive(!string.IsNullOrEmpty(shape));
            }

            EnsureButton();
            if (_button != null)
            {
                _button.interactable = onClick != null;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_dragEnabled)
            {
                return;
            }

            _dragging = true;
            _originalParent = transform.parent;
            _originalAnchoredPosition = _rect.anchoredPosition;
            _dragCanvas = GetComponentInParent<Canvas>();
            transform.SetParent(_dragCanvas != null ? _dragCanvas.transform : transform.root, true);
            transform.SetAsLastSibling();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.alpha = 0.82f;
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
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;
            if (_originalParent != null)
            {
                transform.SetParent(_originalParent, true);
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

        private void EnsureButton()
        {
            if (_button != null)
            {
                return;
            }

            _button = GetComponent<Button>();
            if (_button == null)
            {
                _button = gameObject.AddComponent<Button>();
            }

            if (_button.targetGraphic == null)
            {
                _button.targetGraphic = GetComponentInChildren<Graphic>(true);
            }
        }
    }
}
