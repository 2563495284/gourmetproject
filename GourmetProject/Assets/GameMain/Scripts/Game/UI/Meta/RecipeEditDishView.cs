using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 编辑菜谱态中的单个菜品卡。卡片支持拖拽，Drop 目标负责执行移动或删除。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class RecipeEditDishView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _shapeText;

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Transform _originalParent;
        private Vector2 _originalAnchoredPosition;
        private Canvas _dragCanvas;

        public int BookIndex { get; private set; }
        public int DishIndex { get; private set; }

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _rect = (RectTransform)transform;
        }

        public void Bind(string name, string shape, int bookIndex, int dishIndex)
        {
            BookIndex = bookIndex;
            DishIndex = dishIndex;

            if (_nameText != null)
            {
                _nameText.text = name ?? string.Empty;
            }

            if (_shapeText != null)
            {
                _shapeText.text = shape ?? string.Empty;
                _shapeText.gameObject.SetActive(!string.IsNullOrEmpty(shape));
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
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
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.alpha = 1f;
            if (_originalParent != null)
            {
                transform.SetParent(_originalParent, true);
                _rect.anchoredPosition = _originalAnchoredPosition;
            }
        }
    }
}
