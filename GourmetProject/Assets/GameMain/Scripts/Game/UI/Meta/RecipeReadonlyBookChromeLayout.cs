using UnityEngine;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        [Header("Viewport Driven Chrome")]
        [SerializeField] private RectTransform _frameRect;
        [SerializeField] private RectTransform _paperRect;
        [SerializeField] private RectTransform _titleTabRect;

        [Tooltip("x=left, y=bottom, z=right, w=top")]
        [SerializeField] private Vector4 _frameViewportMargins =
            new(48f, 104.5f, 48f, 124.5f);

        [Tooltip("x=left, y=bottom, z=right, w=top")]
        [SerializeField] private Vector4 _paperViewportMargins =
            new(10f, 52.5f, 10f, 72.5f);

        [SerializeField, Min(0.01f)] private float _titleTabWidthRatio = 0.45f;
        [SerializeField, Min(1f)] private float _titleTabHeight = 128f;
        [SerializeField] private float _titleTabCenterOffset = 120.5f;
        [SerializeField] private float _backButtonCenterOffset = -54.5f;

        private readonly Vector3[] _viewportWorldCorners = new Vector3[4];
        private bool _applyingChromeLayout;

        private void OnRectTransformDimensionsChange()
        {
            ApplyViewportDrivenChromeLayout();
        }

        private void HandleWarehouseViewportDimensionsChanged(
            RectTransform viewport)
        {
            ApplyViewportDrivenChromeLayout(viewport);
        }

        private void ApplyViewportDrivenChromeLayout()
        {
            ApplyViewportDrivenChromeLayout(_warehouse?.ViewportRect);
        }

        private void ApplyViewportDrivenChromeLayout(RectTransform viewport)
        {
            if (_applyingChromeLayout || viewport == null)
            {
                return;
            }

            var root = transform as RectTransform;
            if (root == null)
            {
                return;
            }

            _applyingChromeLayout = true;
            try
            {
                Canvas.ForceUpdateCanvases();
                viewport.GetWorldCorners(_viewportWorldCorners);
                Vector2 min = root.InverseTransformPoint(
                    _viewportWorldCorners[0]);
                Vector2 max = min;
                for (int i = 1; i < _viewportWorldCorners.Length; i++)
                {
                    Vector2 point = root.InverseTransformPoint(
                        _viewportWorldCorners[i]);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                Vector2 viewportCenter = (min + max) * 0.5f;
                Vector2 viewportSize = max - min;
                Vector2 anchorCenter = root.rect.center;
                SetCenteredRect(
                    _frameRect,
                    viewportCenter - anchorCenter,
                    Expand(viewportSize, _frameViewportMargins));
                SetCenteredRect(
                    _paperRect,
                    viewportCenter - anchorCenter,
                    Expand(viewportSize, _paperViewportMargins));

                SetCenteredRect(
                    _titleTabRect,
                    new Vector2(
                        viewportCenter.x - anchorCenter.x,
                        max.y + _titleTabCenterOffset - anchorCenter.y),
                    new Vector2(
                        viewportSize.x * _titleTabWidthRatio,
                        _titleTabHeight));

                RectTransform backRect = _backButton != null
                    ? _backButton.transform as RectTransform
                    : null;
                if (backRect != null)
                {
                    SetCenteredPosition(
                        backRect,
                        new Vector2(
                            viewportCenter.x - anchorCenter.x,
                            min.y + _backButtonCenterOffset - anchorCenter.y));
                }
            }
            finally
            {
                _applyingChromeLayout = false;
            }
        }

        private static Vector2 Expand(Vector2 size, Vector4 margins)
        {
            return new Vector2(
                size.x + margins.x + margins.z,
                size.y + margins.y + margins.w);
        }

        private static void SetCenteredRect(
            RectTransform rect,
            Vector2 center,
            Vector2 size)
        {
            if (rect == null)
            {
                return;
            }

            SetCenteredPosition(rect, center);
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                Mathf.Max(1f, size.x));
            rect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(1f, size.y));
        }

        private static void SetCenteredPosition(
            RectTransform rect,
            Vector2 center)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
        }
    }
}
