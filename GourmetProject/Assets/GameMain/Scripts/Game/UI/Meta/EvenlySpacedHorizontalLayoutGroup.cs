using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// Evenly distributes child centers across the available width. It can optionally
    /// size every child to the same square, constrained by the available space and a
    /// configurable minimum/maximum size.
    /// </summary>
    [AddComponentMenu("Layout/Evenly Spaced Horizontal Layout Group")]
    public sealed class EvenlySpacedHorizontalLayoutGroup : LayoutGroup
    {
        [SerializeField] private float _spacing = 120f;
        [SerializeField] private bool _controlChildSquareSize;
        [SerializeField, Min(0f)] private float _minChildSize = 180f;
        [SerializeField, Min(0f)] private float _maxChildSize = 300f;

        public float spacing
        {
            get => _spacing;
            set => SetProperty(ref _spacing, value);
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();

            if (_controlChildSquareSize)
            {
                float totalSpacing = _spacing * Mathf.Max(0, rectChildren.Count - 1);
                float minWidth = padding.horizontal
                    + _minChildSize * rectChildren.Count
                    + totalSpacing;
                float preferredWidth = padding.horizontal
                    + MaxChildSize * rectChildren.Count
                    + totalSpacing;
                SetLayoutInputForAxis(minWidth, preferredWidth, -1f, 0);
                return;
            }

            float requiredWidth = padding.horizontal + _spacing * Mathf.Max(0, rectChildren.Count - 1);
            for (int i = 0; i < rectChildren.Count; i++)
            {
                requiredWidth += rectChildren[i].rect.width;
            }

            SetLayoutInputForAxis(requiredWidth, requiredWidth, -1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
            if (_controlChildSquareSize)
            {
                SetLayoutInputForAxis(
                    padding.vertical + _minChildSize,
                    padding.vertical + MaxChildSize,
                    -1f,
                    1);
                return;
            }

            float requiredHeight = padding.vertical;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                requiredHeight = Mathf.Max(
                    requiredHeight,
                    padding.vertical + rectChildren[i].rect.height);
            }

            SetLayoutInputForAxis(requiredHeight, requiredHeight, -1f, 1);
        }

        public override void SetLayoutHorizontal()
        {
            int count = rectChildren.Count;
            if (count == 0)
            {
                return;
            }

            float availableWidth = rectTransform.rect.width - padding.horizontal;
            float totalSpacing = spacing * Mathf.Max(0, count - 1);
            float cellWidth = Mathf.Max(0f, availableWidth - totalSpacing) / count;

            if (_controlChildSquareSize)
            {
                float childSize = CalculateSquareChildSize(count);
                float alignment = GetAlignmentOnAxis(0);
                for (int i = 0; i < count; i++)
                {
                    float cellStart = padding.left + (cellWidth + spacing) * i;
                    float childPosition = cellStart + (cellWidth - childSize) * alignment;
                    SetChildAlongAxis(rectChildren[i], 0, childPosition, childSize);
                }

                return;
            }

            for (int i = 0; i < count; i++)
            {
                RectTransform child = rectChildren[i];
                float cellCenter = padding.left
                    + cellWidth * (i + 0.5f)
                    + spacing * i;
                Rect parentRect = rectTransform.rect;
                float desiredPivotX = parentRect.xMin
                    + cellCenter
                    + (child.pivot.x - 0.5f) * child.rect.width;
                float minAnchorX = Mathf.Lerp(parentRect.xMin, parentRect.xMax, child.anchorMin.x);
                float maxAnchorX = Mathf.Lerp(parentRect.xMin, parentRect.xMax, child.anchorMax.x);
                float anchorReferenceX = Mathf.Lerp(minAnchorX, maxAnchorX, child.pivot.x);

                Vector2 anchoredPosition = child.anchoredPosition;
                anchoredPosition.x = desiredPivotX - anchorReferenceX;
                child.anchoredPosition = anchoredPosition;
            }
        }

        public override void SetLayoutVertical()
        {
            Rect parentRect = rectTransform.rect;
            float availableHeight = parentRect.height - padding.vertical;

            if (_controlChildSquareSize)
            {
                float childSize = CalculateSquareChildSize(rectChildren.Count);
                float childPosition = padding.top
                    + (availableHeight - childSize) * GetAlignmentOnAxis(1);
                for (int i = 0; i < rectChildren.Count; i++)
                {
                    SetChildAlongAxis(rectChildren[i], 1, childPosition, childSize);
                }

                return;
            }

            float centerFromBottom = padding.bottom + availableHeight * 0.5f;

            for (int i = 0; i < rectChildren.Count; i++)
            {
                RectTransform child = rectChildren[i];
                float desiredPivotY = parentRect.yMin
                    + centerFromBottom
                    + (child.pivot.y - 0.5f) * child.rect.height;
                float minAnchorY = Mathf.Lerp(parentRect.yMin, parentRect.yMax, child.anchorMin.y);
                float maxAnchorY = Mathf.Lerp(parentRect.yMin, parentRect.yMax, child.anchorMax.y);
                float anchorReferenceY = Mathf.Lerp(minAnchorY, maxAnchorY, child.pivot.y);

                Vector2 anchoredPosition = child.anchoredPosition;
                anchoredPosition.y = desiredPivotY - anchorReferenceY;
                child.anchoredPosition = anchoredPosition;
            }
        }

        private float MaxChildSize => Mathf.Max(_minChildSize, _maxChildSize);

        private float CalculateSquareChildSize(int count)
        {
            if (count <= 0)
            {
                return 0f;
            }

            float availableWidth = Mathf.Max(0f, rectTransform.rect.width - padding.horizontal);
            float availableHeight = Mathf.Max(0f, rectTransform.rect.height - padding.vertical);
            float totalSpacing = spacing * Mathf.Max(0, count - 1);
            float widthPerChild = Mathf.Max(0f, availableWidth - totalSpacing) / count;
            float fittedSize = Mathf.Min(widthPerChild, availableHeight);
            return Mathf.Clamp(fittedSize, _minChildSize, MaxChildSize);
        }
    }
}
