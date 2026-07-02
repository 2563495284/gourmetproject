using UnityEngine;
using UnityEngine.Rendering;

namespace GourmetProject.Runtime.Rendering
{
    /// <summary>
    /// 自包含的「世界模糊捕获」：用一台复制主相机视角的离屏相机，把世界场景渲染到一张低分辨率
    /// RenderTexture，再做 Dual-Kawase 模糊，产出 <see cref="BlurTexture"/> 供 UI 毛玻璃着色器
    /// 按屏幕坐标采样。相比走 SRP RenderGraph 的 RenderFeature，本方案在 URP 2D Renderer 下
    /// 行为确定、成本可控（低分辨率单条 blit 链），不依赖管线注入时机。
    /// 挂在主相机（BattleCamera）上即可。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class WorldBlurCapture : MonoBehaviour
    {
        private static readonly int FrostedGlassTexId = Shader.PropertyToID("_FrostedGlassTex");
        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");

        [SerializeField] private Material blurMaterial;
        [SerializeField, Range(1, 8)] private int downsample = 4;
        [SerializeField, Range(0, 6)] private int iterations = 3;
        [SerializeField, Range(0f, 4f)] private float blurRadius = 1f;

        /// <summary>最近一帧产出的世界模糊贴图，供 <c>UIFrostedGlass</c> 组件绑到材质上。</summary>
        public static Texture BlurTexture { get; private set; }

        private UnityEngine.Camera _mainCamera;
        private UnityEngine.Camera _blurCamera;
        private RenderTexture _sceneTarget;
        private RenderTexture _blurTarget;
        private int _rtWidth;
        private int _rtHeight;

        private void OnEnable()
        {
            _mainCamera = GetComponent<UnityEngine.Camera>();
            EnsureBlurCamera();
        }

        private void OnDisable()
        {
            BlurTexture = null;
            ReleaseTargets();

            if (_blurCamera != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_blurCamera.gameObject);
                }
                else
                {
                    DestroyImmediate(_blurCamera.gameObject);
                }

                _blurCamera = null;
            }
        }

        private void LateUpdate()
        {
            if (blurMaterial == null || _mainCamera == null)
            {
                return;
            }

            EnsureBlurCamera();
            if (!EnsureTargets())
            {
                return;
            }

            SyncBlurCameraToMain();

            if (!RenderScene(_sceneTarget))
            {
                return;
            }

            Blur(_sceneTarget, _blurTarget);

            BlurTexture = _blurTarget;
            Shader.SetGlobalTexture(FrostedGlassTexId, _blurTarget);
        }

        private void EnsureBlurCamera()
        {
            if (_blurCamera != null)
            {
                return;
            }

            var go = new GameObject("~WorldBlurCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            go.transform.SetParent(transform, false);

            _blurCamera = go.AddComponent<UnityEngine.Camera>();
            _blurCamera.enabled = false; // 手动 SubmitRenderRequest / Render 驱动
        }

        private void SyncBlurCameraToMain()
        {
            _blurCamera.transform.SetPositionAndRotation(
                _mainCamera.transform.position, _mainCamera.transform.rotation);
            _blurCamera.orthographic = _mainCamera.orthographic;
            _blurCamera.orthographicSize = _mainCamera.orthographicSize;
            _blurCamera.fieldOfView = _mainCamera.fieldOfView;
            _blurCamera.aspect = _mainCamera.aspect;
            _blurCamera.nearClipPlane = _mainCamera.nearClipPlane;
            _blurCamera.farClipPlane = _mainCamera.farClipPlane;
            _blurCamera.cullingMask = _mainCamera.cullingMask;
            _blurCamera.clearFlags = _mainCamera.clearFlags;
            _blurCamera.backgroundColor = _mainCamera.backgroundColor;
        }

        private bool RenderScene(RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(_blurCamera, request))
            {
                request.destination = target;
                RenderPipeline.SubmitRenderRequest(_blurCamera, request);
                return true;
            }

            // 非 SRP 或不支持渲染请求时的兜底。
            _blurCamera.targetTexture = target;
            _blurCamera.Render();
            _blurCamera.targetTexture = null;
            return true;
        }

        private void Blur(RenderTexture source, RenderTexture dest)
        {
            blurMaterial.SetFloat(BlurRadiusId, blurRadius);

            if (iterations <= 0)
            {
                Graphics.Blit(source, dest);
                return;
            }

            var mips = new RenderTexture[iterations];
            RenderTexture last = source;
            int w = source.width;
            int h = source.height;

            for (int i = 0; i < iterations; i++)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
                mips[i] = RenderTexture.GetTemporary(w, h, 0, source.format);
                mips[i].filterMode = FilterMode.Bilinear;
                Graphics.Blit(last, mips[i], blurMaterial, 0); // Dual-Kawase 降采样
                last = mips[i];
            }

            for (int i = iterations - 2; i >= 0; i--)
            {
                Graphics.Blit(last, mips[i], blurMaterial, 1); // Dual-Kawase 升采样
                last = mips[i];
            }

            Graphics.Blit(last, dest, blurMaterial, 1);

            for (int i = 0; i < iterations; i++)
            {
                RenderTexture.ReleaseTemporary(mips[i]);
            }
        }

        private bool EnsureTargets()
        {
            int width = Mathf.Max(1, Screen.width / Mathf.Max(1, downsample));
            int height = Mathf.Max(1, Screen.height / Mathf.Max(1, downsample));

            if (_sceneTarget != null && _blurTarget != null && width == _rtWidth && height == _rtHeight)
            {
                return true;
            }

            ReleaseTargets();

            _rtWidth = width;
            _rtHeight = height;

            _sceneTarget = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = "_WorldBlurScene",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _sceneTarget.Create();

            _blurTarget = new RenderTexture(width, height, 0, RenderTextureFormat.DefaultHDR)
            {
                name = "_FrostedGlassTex",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _blurTarget.Create();

            return true;
        }

        private void ReleaseTargets()
        {
            if (_sceneTarget != null)
            {
                _sceneTarget.Release();
                if (Application.isPlaying)
                {
                    Destroy(_sceneTarget);
                }
                else
                {
                    DestroyImmediate(_sceneTarget);
                }

                _sceneTarget = null;
            }

            if (_blurTarget != null)
            {
                _blurTarget.Release();
                if (Application.isPlaying)
                {
                    Destroy(_blurTarget);
                }
                else
                {
                    DestroyImmediate(_blurTarget);
                }

                _blurTarget = null;
            }

            _rtWidth = 0;
            _rtHeight = 0;
        }
    }
}
