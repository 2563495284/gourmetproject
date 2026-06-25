using UnityEngine;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>
    /// Keeps a RectTransform in a fixed design resolution and scales it uniformly to fit its parent.
    /// Useful when child UI should preserve relative size and position against a background image.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ReferenceResolutionScaler : MonoBehaviour
    {
        [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
        [SerializeField] private RectTransform fitParent;

        private RectTransform _rectTransform;
        private bool _isApplying;

        private RectTransform CachedRectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = GetComponent<RectTransform>();
                }

                return _rectTransform;
            }
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (_isApplying || referenceResolution.x <= 0f || referenceResolution.y <= 0f)
            {
                return;
            }

            RectTransform rectTransform = CachedRectTransform;
            RectTransform parent = fitParent != null ? fitParent : rectTransform.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Rect parentRect = parent.rect;
            if (parentRect.width <= 0f || parentRect.height <= 0f)
            {
                return;
            }

            float scale = Mathf.Min(parentRect.width / referenceResolution.x, parentRect.height / referenceResolution.y);

            _isApplying = true;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = referenceResolution;
            rectTransform.localScale = new Vector3(scale, scale, 1f);
            _isApplying = false;
        }
    }
}
