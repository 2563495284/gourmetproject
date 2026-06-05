using UnityEngine;

namespace GourmetProject.Runtime.Camera
{
    /// <summary>
    /// Fits an orthographic camera against a reference resolution so gameplay keeps a stable design area.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class OrthographicCameraFitter : MonoBehaviour
    {
        public enum FitMode
        {
            /// <summary>Never crops the reference area; wider screens see more width, narrower screens see more height.</summary>
            FitReferenceArea = 0,

            /// <summary>Keeps vertical world size fixed; aspect changes only affect visible width.</summary>
            FixedHeight = 1,

            /// <summary>Keeps horizontal world size fixed; aspect changes only affect visible height.</summary>
            FixedWidth = 2,
        }

        [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
        [SerializeField] private float referenceOrthographicSize = 5.4f;
        [SerializeField] private FitMode fitMode = FitMode.FitReferenceArea;

        private UnityEngine.Camera _camera;
        private int _lastPixelWidth;
        private int _lastPixelHeight;
        private bool _lastOrthographic;

        private UnityEngine.Camera CachedCamera
        {
            get
            {
                if (_camera == null)
                {
                    _camera = GetComponent<UnityEngine.Camera>();
                }

                return _camera;
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

        private void LateUpdate()
        {
            UnityEngine.Camera camera = CachedCamera;
            if (camera.pixelWidth != _lastPixelWidth || camera.pixelHeight != _lastPixelHeight || camera.orthographic != _lastOrthographic)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (referenceResolution.x <= 0f || referenceResolution.y <= 0f || referenceOrthographicSize <= 0f)
            {
                return;
            }

            UnityEngine.Camera camera = CachedCamera;
            _lastPixelWidth = camera.pixelWidth;
            _lastPixelHeight = camera.pixelHeight;
            _lastOrthographic = camera.orthographic;

            if (!camera.orthographic || camera.pixelWidth <= 0 || camera.pixelHeight <= 0)
            {
                return;
            }

            float referenceAspect = referenceResolution.x / referenceResolution.y;
            float currentAspect = (float)camera.pixelWidth / camera.pixelHeight;

            camera.orthographicSize = fitMode switch
            {
                FitMode.FixedHeight => referenceOrthographicSize,
                FitMode.FixedWidth => referenceOrthographicSize * referenceAspect / currentAspect,
                _ => currentAspect < referenceAspect
                    ? referenceOrthographicSize * referenceAspect / currentAspect
                    : referenceOrthographicSize,
            };
        }
    }
}
