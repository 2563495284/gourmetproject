using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// Keeps a scrollbar handle at a fixed visual height and synchronizes its
    /// value with a ScrollRect without letting ScrollRect rewrite its size.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Scrollbar))]
    public sealed class FixedScrollbarHandleSize : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField, Min(1f)] private float _handleHeight = 199f;

        private Scrollbar _scrollbar;

        private void Awake()
        {
            ResolveReferences();
            ApplyHandleSize();
        }

        private void OnEnable()
        {
            ResolveReferences();
            _scrollbar.onValueChanged.AddListener(OnScrollbarValueChanged);
            if (_scrollRect != null)
            {
                _scrollRect.onValueChanged.AddListener(OnScrollRectValueChanged);
            }
            Canvas.willRenderCanvases += ApplyHandleSize;
            SyncFromScrollRect();
            ApplyHandleSize();
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= ApplyHandleSize;

            if (_scrollbar != null)
            {
                _scrollbar.onValueChanged.RemoveListener(OnScrollbarValueChanged);
            }

            if (_scrollRect != null)
            {
                _scrollRect.onValueChanged.RemoveListener(OnScrollRectValueChanged);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveReferences();
            SyncFromScrollRect();
            ApplyHandleSize();
        }
#endif

        private void ResolveReferences()
        {
            if (_scrollbar == null)
            {
                _scrollbar = GetComponent<Scrollbar>();
            }

            if (_scrollRect == null)
            {
                _scrollRect = GetComponentInParent<ScrollRect>();
            }
        }

        private void OnScrollbarValueChanged(float value)
        {
            if (_scrollRect != null)
            {
                _scrollRect.verticalNormalizedPosition = value;
            }
        }

        private void OnScrollRectValueChanged(Vector2 normalizedPosition)
        {
            if (_scrollbar != null)
            {
                _scrollbar.SetValueWithoutNotify(normalizedPosition.y);
            }
        }

        private void SyncFromScrollRect()
        {
            if (_scrollbar != null && _scrollRect != null)
            {
                _scrollbar.SetValueWithoutNotify(_scrollRect.verticalNormalizedPosition);
            }
        }

        private void ApplyHandleSize()
        {
            if (_scrollbar == null || _scrollbar.handleRect == null)
            {
                return;
            }

            var slidingArea = _scrollbar.handleRect.parent as RectTransform;
            if (slidingArea == null || slidingArea.rect.height <= 0f)
            {
                return;
            }

            float handleRatio = Mathf.Clamp01(_handleHeight / slidingArea.rect.height);
            if (!Mathf.Approximately(_scrollbar.size, handleRatio))
            {
                _scrollbar.size = handleRatio;
            }
        }
    }
}
