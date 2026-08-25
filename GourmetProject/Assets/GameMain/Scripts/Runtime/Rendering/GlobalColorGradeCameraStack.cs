using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GourmetProject.Runtime.Rendering
{
    /// <summary>
    /// Keeps the persistent UI overlay camera attached to the currently active base camera.
    /// The overlay camera renders the root Canvas and performs the final full-frame color grade.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class GlobalColorGradeCameraStack : MonoBehaviour
    {
        private const string MainCameraTag = "MainCamera";

        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private UnityEngine.Camera uiCamera;
        [SerializeField, Min(0.01f)] private float canvasPlaneDistance = 1f;

        private UnityEngine.Camera _baseCamera;
        private UniversalAdditionalCameraData _baseCameraData;
        private UniversalAdditionalCameraData _uiCameraData;
        private UnityEngine.Camera[] _cameraBuffer = new UnityEngine.Camera[4];

        private void Reset()
        {
            uiCanvas = GetComponent<Canvas>();
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureUiCamera();
            RebindToActiveBaseCamera();
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            RebindToActiveBaseCamera();
        }

        private void Start()
        {
            RebindToActiveBaseCamera();
        }

        private void LateUpdate()
        {
            UnityEngine.Camera activeBaseCamera = FindActiveBaseCamera();
            if (activeBaseCamera != _baseCamera)
            {
                BindTo(activeBaseCamera);
                return;
            }

            SyncCameraSettings();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            DetachFromBaseCamera();
        }

        private void OnDestroy()
        {
            DetachFromBaseCamera();
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            RebindToActiveBaseCamera();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            RebindToActiveBaseCamera();
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            RebindToActiveBaseCamera();
        }

        private void ResolveReferences()
        {
            if (uiCanvas == null)
            {
                uiCanvas = GetComponent<Canvas>();
            }

            if (uiCamera != null)
            {
                uiCamera.TryGetComponent(out _uiCameraData);
            }
        }

        private void ConfigureUiCamera()
        {
            if (uiCanvas == null || uiCamera == null || _uiCameraData == null)
            {
                Debug.LogError(
                    "GlobalColorGradeCameraStack requires a root Canvas and a URP UI camera.",
                    this);
                enabled = false;
                return;
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0)
            {
                Debug.LogError("The project does not define the built-in UI layer.", this);
                enabled = false;
                return;
            }

            uiCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            uiCanvas.worldCamera = uiCamera;
            uiCanvas.planeDistance = canvasPlaneDistance;

            uiCamera.cullingMask = 1 << uiLayer;
            uiCamera.targetTexture = null;
            uiCamera.enabled = true;

            _uiCameraData.renderType = CameraRenderType.Overlay;
            _uiCameraData.renderPostProcessing = true;
            _uiCameraData.renderShadows = false;
            _uiCameraData.volumeLayerMask = 1 << 0;
        }

        private void RebindToActiveBaseCamera()
        {
            BindTo(FindActiveBaseCamera());
        }

        private void BindTo(UnityEngine.Camera nextBaseCamera)
        {
            if (nextBaseCamera == _baseCamera)
            {
                SyncCameraSettings();
                return;
            }

            DetachFromBaseCamera();

            if (nextBaseCamera == null || uiCamera == null)
            {
                return;
            }

            if (!nextBaseCamera.TryGetComponent(out UniversalAdditionalCameraData nextBaseData)
                || nextBaseData.renderType != CameraRenderType.Base)
            {
                return;
            }

            _baseCamera = nextBaseCamera;
            _baseCameraData = nextBaseData;

            if (!_baseCameraData.cameraStack.Contains(uiCamera))
            {
                _baseCameraData.cameraStack.Add(uiCamera);
            }

            SyncCameraSettings();
        }

        private void DetachFromBaseCamera()
        {
            if (_baseCameraData != null && uiCamera != null)
            {
                _baseCameraData.cameraStack.Remove(uiCamera);
            }

            _baseCamera = null;
            _baseCameraData = null;
        }

        private void SyncCameraSettings()
        {
            if (_baseCamera == null || uiCamera == null)
            {
                return;
            }

            uiCamera.transform.SetPositionAndRotation(
                _baseCamera.transform.position,
                _baseCamera.transform.rotation);
            uiCamera.rect = _baseCamera.rect;
            uiCamera.targetDisplay = _baseCamera.targetDisplay;
            uiCamera.orthographic = _baseCamera.orthographic;
            uiCamera.orthographicSize = _baseCamera.orthographicSize;
            uiCamera.fieldOfView = _baseCamera.fieldOfView;
            uiCamera.nearClipPlane = _baseCamera.nearClipPlane;
            uiCamera.farClipPlane = _baseCamera.farClipPlane;
            uiCamera.allowHDR = _baseCamera.allowHDR;
            uiCamera.allowMSAA = _baseCamera.allowMSAA;
            uiCamera.allowDynamicResolution = _baseCamera.allowDynamicResolution;

            if (uiCanvas != null)
            {
                uiCanvas.worldCamera = uiCamera;
                uiCanvas.planeDistance = Mathf.Clamp(
                    canvasPlaneDistance,
                    uiCamera.nearClipPlane + 0.01f,
                    uiCamera.farClipPlane - 0.01f);
            }
        }

        private UnityEngine.Camera FindActiveBaseCamera()
        {
            UnityEngine.SceneManagement.Scene activeScene = SceneManager.GetActiveScene();
            UnityEngine.Camera fallback = null;
            int cameraCount = UnityEngine.Camera.allCamerasCount;
            if (_cameraBuffer.Length < cameraCount)
            {
                _cameraBuffer = new UnityEngine.Camera[Mathf.NextPowerOfTwo(cameraCount)];
            }

            cameraCount = UnityEngine.Camera.GetAllCameras(_cameraBuffer);

            for (int i = 0; i < cameraCount; i++)
            {
                UnityEngine.Camera candidate = _cameraBuffer[i];
                if (candidate == null
                    || candidate == uiCamera
                    || !candidate.enabled
                    || !candidate.gameObject.activeInHierarchy
                    || !candidate.CompareTag(MainCameraTag)
                    || !candidate.TryGetComponent(out UniversalAdditionalCameraData cameraData)
                    || cameraData.renderType != CameraRenderType.Base)
                {
                    continue;
                }

                if (candidate.gameObject.scene == activeScene)
                {
                    return candidate;
                }

                fallback ??= candidate;
            }

            return fallback;
        }
    }
}
