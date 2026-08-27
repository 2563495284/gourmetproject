using UnityEngine;

namespace GourmetProject.Runtime.Camera
{
    /// <summary>
    /// Uniformly scales a <see cref="SpriteRenderer"/> so it always covers the target orthographic
    /// camera viewport (cover mode: fills entirely, cropping overflow on the longer axis).
    /// Runs in edit and play mode and re-fits when the viewport (orthographic size / aspect) changes.
    /// Self-contained scene backdrop helper — attach to the background sprite; no gameplay dependency.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteCameraCover : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera _camera;
        [SerializeField] private SpriteRenderer _renderer;

        private float _lastOrthoSize;
        private float _lastAspect;
        private Sprite _lastSprite;
        private bool _fitted;

        private UnityEngine.Camera CachedCamera
        {
            get
            {
                if (_camera == null)
                {
                    _camera = UnityEngine.Camera.main;
                }

                return _camera;
            }
        }

        private SpriteRenderer CachedRenderer
        {
            get
            {
                if (_renderer == null)
                {
                    _renderer = GetComponent<SpriteRenderer>();
                }

                return _renderer;
            }
        }

        private void OnEnable()
        {
            Apply(true);
        }

        private void OnValidate()
        {
            Apply(true);
        }

        private void LateUpdate()
        {
            Apply(false);
        }

        private void Apply(bool force)
        {
            UnityEngine.Camera camera = CachedCamera;
            SpriteRenderer renderer = CachedRenderer;
            if (camera == null || renderer == null || renderer.sprite == null || !camera.orthographic)
            {
                return;
            }

            float orthoSize = camera.orthographicSize;
            float aspect = camera.aspect;
            if (orthoSize <= 0f || aspect <= 0f)
            {
                return;
            }

            if (!force
                && _fitted
                && renderer.sprite == _lastSprite
                && Mathf.Approximately(orthoSize, _lastOrthoSize)
                && Mathf.Approximately(aspect, _lastAspect))
            {
                return;
            }

            Vector2 spriteSize = renderer.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            {
                return;
            }

            float height = orthoSize * 2f;
            float width = height * aspect;
            float scale = Mathf.Max(width / spriteSize.x, height / spriteSize.y);

            Vector3 local = transform.localScale;
            var target = new Vector3(scale, scale, Mathf.Approximately(local.z, 0f) ? 1f : local.z);
            if (transform.localScale != target)
            {
                transform.localScale = target;
            }

            _lastOrthoSize = orthoSize;
            _lastAspect = aspect;
            _lastSprite = renderer.sprite;
            _fitted = true;
        }
    }
}
