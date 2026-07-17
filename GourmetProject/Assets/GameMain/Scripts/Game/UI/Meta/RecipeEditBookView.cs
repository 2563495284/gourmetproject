using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 编辑菜谱态中的一本菜谱。作为 Drop 目标接收从其它菜谱拖来的菜品。
    /// </summary>
    public sealed class RecipeEditBookView : MonoBehaviour, IDropHandler
    {
        private const float LayoutTweenDuration = 0.2f;

        [SerializeField] private RectTransform _dishContainer;
        [SerializeField] private Vector2 _cellSize = new Vector2(96f, 96f);
        [SerializeField] private Vector2 _spacing = new Vector2(8f, 8f);
        [SerializeField] private int _columnCount = 2;
        [SerializeField] private RectOffset _padding;
        [SerializeField] private ScrollRect _scrollRect;

        private int _bookIndex;
        private Action<RecipeEditDishView, int, int> _onDishDropped;
        private Action<RewardDishChoiceCardView, int> _onChoiceDropped;
        private GridLayoutGroup _grid;
        private int _fitSlotCapacity;

        public RectTransform DishContainer => _dishContainer;

        private void Awake()
        {
            ResolveLayout();
        }

        public void Bind(
            int bookIndex,
            Action<RecipeEditDishView, int, int> onDishDropped,
            Action<RewardDishChoiceCardView, int> onChoiceDropped = null)
        {
            ResolveLayout();
            _bookIndex = bookIndex;
            _onDishDropped = onDishDropped;
            _onChoiceDropped = onChoiceDropped;
            ApplyImmediateLayout();
        }

        public void OnDrop(PointerEventData eventData)
        {
            RecipeEditDishView dish = eventData.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponentInParent<RecipeEditDishView>();
            if (dish != null)
            {
                _onDishDropped?.Invoke(dish, _bookIndex, DropIndex(eventData));
                return;
            }

            RewardDishChoiceCardView choice = eventData.pointerDrag == null
                ? null
                : eventData.pointerDrag.GetComponentInParent<RewardDishChoiceCardView>();
            if (choice != null)
            {
                _onChoiceDropped?.Invoke(choice, _bookIndex);
            }
        }

        public void ApplyImmediateLayout(RecipeEditDishView exclude = null)
        {
            ResolveLayout();
            int index = 0;
            foreach (RecipeEditDishView child in Dishes())
            {
                if (child == null || child == exclude)
                {
                    continue;
                }

                RectTransform rect = (RectTransform)child.transform;
                ConfigureDishRect(rect);
                rect.anchoredPosition = SlotAnchoredPosition(index);
                rect.sizeDelta = _cellSize;
                index++;
            }

            UpdateContentSize(index);
        }

        public void FitSlotsWithinView(int slotCapacity)
        {
            _fitSlotCapacity = Mathf.Max(0, slotCapacity);
            ResolveLayout();
        }

        private void ApplySlotFit()
        {
            if (_dishContainer == null || _fitSlotCapacity <= 0)
            {
                return;
            }

            int columns = Mathf.Max(1, _columnCount);
            int rows = Mathf.CeilToInt(_fitSlotCapacity / (float)columns);
            if (rows <= 0)
            {
                return;
            }

            Vector2 available = _dishContainer.rect.size;
            if (available.x <= 0f || available.y <= 0f)
            {
                return;
            }

            float desiredWidth = _padding.left + _padding.right + columns * _cellSize.x + Mathf.Max(0, columns - 1) * _spacing.x;
            float desiredHeight = _padding.top + _padding.bottom + rows * _cellSize.y + Mathf.Max(0, rows - 1) * _spacing.y;
            if (desiredWidth <= 0f || desiredHeight <= 0f)
            {
                return;
            }

            float scale = Mathf.Min(available.x / desiredWidth, available.y / desiredHeight, 1f);
            _cellSize *= scale;
            _spacing *= scale;
        }

        public void AnimateCompaction(RecipeEditDishView exclude = null)
        {
            ResolveLayout();
            int index = 0;
            foreach (RecipeEditDishView child in Dishes())
            {
                if (child == null || child == exclude)
                {
                    continue;
                }

                RectTransform rect = (RectTransform)child.transform;
                ConfigureDishRect(rect);
                DOTween.Kill(rect);
                Vector2 target = SlotAnchoredPosition(index);
                DOTween.To(() => rect.anchoredPosition, value => rect.anchoredPosition = value, target, LayoutTweenDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetTarget(rect)
                    .SetLink(rect.gameObject);
                index++;
            }

            UpdateContentSize(index);
        }

        public void AnimateInsertionGap(int insertIndex, RecipeEditDishView exclude = null)
        {
            ResolveLayout();
            insertIndex = Mathf.Max(0, insertIndex);
            int index = 0;
            foreach (RecipeEditDishView child in Dishes())
            {
                if (child == null || child == exclude)
                {
                    continue;
                }

                RectTransform rect = (RectTransform)child.transform;
                ConfigureDishRect(rect);
                int targetIndex = index >= insertIndex ? index + 1 : index;
                DOTween.Kill(rect);
                Vector2 target = SlotAnchoredPosition(targetIndex);
                DOTween.To(() => rect.anchoredPosition, value => rect.anchoredPosition = value, target, LayoutTweenDuration)
                    .SetEase(Ease.OutCubic)
                    .SetUpdate(true)
                    .SetTarget(rect)
                    .SetLink(rect.gameObject);
                index++;
            }

            UpdateContentSize(index + 1);
        }

        public void ScrollToIndex(int index, float duration)
        {
            ResolveLayout();
            if (_scrollRect == null || _scrollRect.content == null || _scrollRect.viewport == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            float contentHeight = Mathf.Max(_scrollRect.content.rect.height, _scrollRect.viewport.rect.height);
            float scrollableHeight = contentHeight - _scrollRect.viewport.rect.height;
            if (scrollableHeight <= 0.01f)
            {
                return;
            }

            int row = Mathf.Max(0, index) / Mathf.Max(1, _columnCount);
            float slotTop = _padding.top + row * (_cellSize.y + _spacing.y);
            float slotBottom = slotTop + _cellSize.y;
            float currentTop = (1f - _scrollRect.verticalNormalizedPosition) * scrollableHeight;
            float targetTop = currentTop;
            if (slotTop < currentTop)
            {
                targetTop = slotTop;
            }
            else if (slotBottom > currentTop + _scrollRect.viewport.rect.height)
            {
                targetTop = slotBottom - _scrollRect.viewport.rect.height;
            }

            targetTop = Mathf.Clamp(targetTop, 0f, scrollableHeight);
            float targetNormalized = 1f - targetTop / scrollableHeight;
            _scrollRect.StopMovement();
            DOTween.Kill(_scrollRect);
            DOTween.To(
                    () => _scrollRect.verticalNormalizedPosition,
                    value => _scrollRect.verticalNormalizedPosition = value,
                    targetNormalized,
                    Mathf.Max(0f, duration))
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetTarget(_scrollRect);
        }

        public Vector3 SlotWorldCenter(int index)
        {
            ResolveLayout();
            if (_dishContainer == null)
            {
                return transform.position;
            }

            Vector2 anchored = SlotAnchoredPosition(index);
            Rect containerRect = _dishContainer.rect;
            Vector2 local = new Vector2(
                -containerRect.width * _dishContainer.pivot.x + anchored.x + _cellSize.x * 0.5f,
                containerRect.height * (1f - _dishContainer.pivot.y) + anchored.y - _cellSize.y * 0.5f);
            return _dishContainer.TransformPoint(local);
        }

        public int CurrentDishCount(RecipeEditDishView exclude = null)
        {
            int count = 0;
            foreach (RecipeEditDishView child in Dishes())
            {
                if (child != null && child != exclude)
                {
                    count++;
                }
            }

            return count;
        }

        private void ResolveLayout()
        {
            if (_dishContainer == null)
            {
                _dishContainer = transform as RectTransform;
            }

            if (_dishContainer == null)
            {
                return;
            }

            _padding ??= new RectOffset(0, 0, 0, 0);
            if (_scrollRect == null)
            {
                _scrollRect = _dishContainer.GetComponentInParent<ScrollRect>(true);
            }

            _grid ??= _dishContainer.GetComponent<GridLayoutGroup>();
            if (_grid == null)
            {
                return;
            }

            _cellSize = _grid.cellSize;
            _spacing = _grid.spacing;
            _columnCount = Mathf.Max(1, _grid.constraintCount);
            if (_grid.padding != null)
            {
                _padding = new RectOffset(_grid.padding.left, _grid.padding.right, _grid.padding.top, _grid.padding.bottom);
            }

            ApplySlotFit();

            _grid.enabled = false;
        }

        private int DropIndex(PointerEventData eventData)
        {
            ResolveLayout();
            int count = CurrentDishCount();
            if (_dishContainer == null)
            {
                return count;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : eventData.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_dishContainer, eventData.position, cam, out Vector2 local))
            {
                return count;
            }

            Rect rect = _dishContainer.rect;
            float x = local.x + rect.width * _dishContainer.pivot.x - HorizontalStartOffset();
            float y = rect.height * (1f - _dishContainer.pivot.y) - local.y - _padding.top;
            int col = Mathf.Clamp(Mathf.FloorToInt(x / Mathf.Max(1f, _cellSize.x + _spacing.x)), 0, Mathf.Max(0, _columnCount - 1));
            int row = Mathf.Max(0, Mathf.FloorToInt(y / Mathf.Max(1f, _cellSize.y + _spacing.y)));
            return Mathf.Clamp(row * Mathf.Max(1, _columnCount) + col, 0, count);
        }

        private Vector2 SlotAnchoredPosition(int index)
        {
            int columns = Mathf.Max(1, _columnCount);
            int col = Mathf.Max(0, index) % columns;
            int row = Mathf.Max(0, index) / columns;
            float startX = HorizontalStartOffset();
            return new Vector2(
                startX + col * (_cellSize.x + _spacing.x),
                -_padding.top - row * (_cellSize.y + _spacing.y));
        }

        private float HorizontalStartOffset()
        {
            if (_dishContainer == null)
            {
                return _padding.left;
            }

            int columns = Mathf.Max(1, _columnCount);
            float rowWidth = columns * _cellSize.x + Mathf.Max(0, columns - 1) * _spacing.x;
            float innerWidth = Mathf.Max(0f, _dishContainer.rect.width - _padding.left - _padding.right);
            return _padding.left + Mathf.Max(0f, (innerWidth - rowWidth) * 0.5f);
        }

        private void UpdateContentSize(int slotCount)
        {
            if (_dishContainer == null || _scrollRect == null || _scrollRect.content != _dishContainer)
            {
                return;
            }

            int columns = Mathf.Max(1, _columnCount);
            int rows = slotCount <= 0 ? 0 : Mathf.CeilToInt(slotCount / (float)columns);
            float contentHeight = _padding.top + _padding.bottom;
            if (rows > 0)
            {
                contentHeight += rows * _cellSize.y + Mathf.Max(0, rows - 1) * _spacing.y;
            }

            Vector2 size = _dishContainer.sizeDelta;
            size.y = Mathf.Max(size.y, contentHeight);
            _dishContainer.sizeDelta = size;
        }

        private void ConfigureDishRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private RecipeEditDishView[] Dishes()
        {
            return _dishContainer == null
                ? Array.Empty<RecipeEditDishView>()
                : _dishContainer.GetComponentsInChildren<RecipeEditDishView>(false);
        }
    }
}
