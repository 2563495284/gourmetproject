using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// Evenly distributes child centers across the available width. This component
    /// only changes anchoredPosition.x; it never changes child width or height.
    /// </summary>
    [AddComponentMenu("Layout/Evenly Spaced Horizontal Layout Group")]
    public sealed class EvenlySpacedHorizontalLayoutGroup : LayoutGroup
    {
        [SerializeField] private float _spacing = 120f;

        public float spacing
        {
            get => _spacing;
            set => SetProperty(ref _spacing, value);
        }

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();

            float requiredWidth = padding.horizontal + _spacing * Mathf.Max(0, rectChildren.Count - 1);
            for (int i = 0; i < rectChildren.Count; i++)
            {
                requiredWidth += rectChildren[i].rect.width;
            }

            SetLayoutInputForAxis(requiredWidth, requiredWidth, -1f, 0);
        }

        public override void CalculateLayoutInputVertical()
        {
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
    }
}
