using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using GourmetProject.Runtime.Pooling;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 甜蜜传递飞行特效。主体、残影与到达点统一写入一个动态 Mesh，
    /// 避免一条飞行拆成数十个 SpriteRenderer / draw call。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    internal sealed class SweetTransferParticleView : MonoBehaviour
    {
        private const string ShaderName = "GourmetProject/SweetTransferParticleBatch";
        private const float ArrivalDotRelativeScale = 0.46f;
        private const float FailureDotRelativeScale = 0.42f;

        [SerializeField] private MeshFilter _meshFilter;
        [SerializeField] private MeshRenderer _meshRenderer;
        [SerializeField] private float _size = 0.22f;
        [SerializeField] private float _minimumArcHeight = 0.22f;
        [SerializeField] private float _arcHeightPerUnit = 0.14f;
        [SerializeField] private Color _color = new(1f, 0.24f, 0.68f, 1f);
        [Header("Afterimage Trail")]
        [SerializeField] private int _afterimageCount = 24;
        [SerializeField] private float _afterimageSpacing = 0.019f;
        [SerializeField, Range(0f, 1f)] private float _afterimageHeadAlpha = 0.48f;
        [SerializeField, Range(0.05f, 1f)] private float _afterimageTailScale = 0.32f;
        [SerializeField] private int _arrivalDotCount = 10;
        [SerializeField] private float _arrivalRadius = 0.36f;

        private static Material s_sharedMaterial;

        private readonly List<Vector3> _vertices = new();
        private readonly List<Color32> _colors = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _triangles = new();

        private Tween _tween;
        private Mesh _mesh;
        private float _visualScale = 1f;
        private Color _defaultColor;
        private bool _hasDefaultColor;
        private int _runtimeAfterimageCount;
        private int _runtimeArrivalDotCount;
        private int _runtimeFailureDotCount;
        private int _afterimageSlotStart;
        private int _failureSlotStart;
        private int _coreSlot;
        private int _arrivalSlotStart;
        private int _quadCount;

        private static Material SharedMaterial
        {
            get
            {
                if (s_sharedMaterial == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/SweetTransferParticleBatch")
                                    ?? Shader.Find(ShaderName);
                    if (shader == null)
                    {
                        throw new InvalidOperationException($"缺少甜蜜传递批渲染 Shader：{ShaderName}");
                    }

                    s_sharedMaterial = new Material(shader)
                    {
                        name = "RuntimeSweetTransferParticleBatch",
                        hideFlags = HideFlags.DontSave,
                    };
                }

                return s_sharedMaterial;
            }
        }

        private void Awake()
        {
            CaptureDefaultColor();
        }

        public static async Awaitable PlayAsync(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float duration,
            CancellationToken cancellationToken,
            Color? colorOverride = null,
            float visualScale = 1f,
            GameObjectPool pool = null)
        {
            SweetTransferParticleView view = Begin(
                prefab,
                parent,
                start,
                end,
                duration,
                colorOverride,
                visualScale,
                pool);
            if (view == null)
            {
                return;
            }

            try
            {
                await view.WaitAsync(cancellationToken);
            }
            finally
            {
                if (view != null)
                {
                    Release(view, pool);
                }
            }
        }

        /// <summary>
        /// 同步创建并启动飞行。不要把起飞放进 async PlayAsync 再逐个 await：
        /// Unity Awaitable 会把启动绑在第一次 await 上，看起来就像一颗飞完才飞下一颗。
        /// </summary>
        internal static SweetTransferParticleView Begin(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float duration,
            Color? colorOverride = null,
            float visualScale = 1f,
            GameObjectPool pool = null)
        {
            if (prefab == null)
            {
                return null;
            }

            SweetTransferParticleView view = pool != null
                ? pool.Get<SweetTransferParticleView>(parent)
                : Instantiate(prefab, parent);
            view.CaptureDefaultColor();
            if (colorOverride.HasValue)
            {
                view._color = colorOverride.Value;
            }

            view._visualScale = Mathf.Max(0.0001f, visualScale);
            view.StartFlight(start, end, duration);
            return view;
        }

        internal Awaitable WaitAsync(CancellationToken cancellationToken)
        {
            return PresentationTween.AwaitCompletionAsync(_tween, cancellationToken);
        }

        public static async Awaitable PlayFailureAsync(
            SweetTransferParticleView prefab,
            Transform parent,
            Vector3 anchor,
            float duration,
            CancellationToken cancellationToken,
            float visualScale = 1f,
            GameObjectPool pool = null)
        {
            if (prefab == null)
            {
                return;
            }

            SweetTransferParticleView view = pool != null
                ? pool.Get<SweetTransferParticleView>(parent)
                : Instantiate(prefab, parent);
            view.CaptureDefaultColor();
            try
            {
                view._visualScale = Mathf.Max(0.0001f, visualScale);
                await view.PlayFailureInternalAsync(anchor, duration, cancellationToken);
            }
            finally
            {
                if (view != null)
                {
                    Release(view, pool);
                }
            }
        }

        private async Awaitable PlayFailureInternalAsync(
            Vector3 anchor,
            float duration,
            CancellationToken cancellationToken)
        {
            EnsureRuntimeObjects();
            float size = _size * _visualScale;
            RenderFailureFrame(anchor, size, 0f);

            _tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), progress =>
                {
                    if (this != null && _meshRenderer != null)
                    {
                        RenderFailureFrame(anchor, size, Mathf.Clamp01(progress));
                    }
                })
                .SetEase(Ease.InCubic)
                .SetLink(gameObject);

            await PresentationTween.AwaitCompletionAsync(_tween, cancellationToken);
        }

        private void StartFlight(Vector3 start, Vector3 end, float duration)
        {
            EnsureRuntimeObjects();

            float size = _size * _visualScale;
            float distance = Vector2.Distance(start, end);
            Vector3 control = (start + end) * 0.5f
                + Vector3.up * Mathf.Max(
                    _minimumArcHeight * _visualScale,
                    distance * _arcHeightPerUnit);
            RenderFlightFrame(start, control, end, size, 0f);

            _tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, duration), progress =>
                {
                    if (this != null && _meshRenderer != null)
                    {
                        RenderFlightFrame(
                            start,
                            control,
                            end,
                            size,
                            Mathf.Clamp01(progress));
                    }
                })
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject);
        }

        private void RenderFlightFrame(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float size,
            float progress)
        {
            float travel = Mathf.Clamp01(progress / 0.80f);
            Vector3 corePosition = Bezier(start, control, end, travel);
            float pulse = Mathf.Sin(travel * Mathf.PI);
            float coreSize = size * Mathf.Lerp(0.55f, 1.22f, pulse);
            PrepareFrame(corePosition);

            float safeAfterimageSpacing = Mathf.Max(0.005f, _afterimageSpacing);
            for (int i = 0; i < _runtimeAfterimageCount; i++)
            {
                float delay = (i + 1) * safeAfterimageSpacing;
                float pointT = travel - delay;
                if (pointT <= 0f || progress >= 0.95f)
                {
                    continue;
                }

                float age = (i + 1f) / (_runtimeAfterimageCount + 1f);
                float echoPulse = Mathf.Sin(pointT * Mathf.PI);
                float echoSize = size * Mathf.Lerp(0.55f, 1.22f, echoPulse);
                float ageScale = Mathf.Lerp(1f, _afterimageTailScale, age);
                float spawnFade = Mathf.Clamp01(pointT / (safeAfterimageSpacing * 1.5f));
                float arrivalFade = Mathf.Clamp01((0.95f - progress) / 0.12f);
                float ageFade = Mathf.Pow(1f - age, 0.9f);
                Color afterimageColor = Color.Lerp(_color, Color.white, (1f - age) * 0.16f);
                afterimageColor.a = _color.a
                    * _afterimageHeadAlpha
                    * ageFade
                    * spawnFade
                    * arrivalFade;
                WriteQuad(
                    _afterimageSlotStart + i,
                    Bezier(start, control, end, pointT),
                    echoSize * ageScale,
                    afterimageColor);
            }

            float fadeIn = Mathf.Clamp01(travel / 0.12f);
            float fadeOut = Mathf.Clamp01((0.88f - progress) / 0.10f);
            Color coreColor = _color;
            coreColor.a *= Mathf.Min(fadeIn, fadeOut);
            WriteQuad(_coreSlot, corePosition, coreSize, coreColor);

            float arrival = Mathf.Clamp01((progress - 0.70f) / 0.30f);
            float radius = Mathf.Lerp(
                size * 0.18f,
                Mathf.Max(size, _arrivalRadius * _visualScale),
                arrival);
            float ringAlpha = Mathf.Sin(arrival * Mathf.PI) * 0.82f;
            Color dotColor = new(1f, 0.78f, 0.94f, ringAlpha);
            for (int i = 0; i < _runtimeArrivalDotCount; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, _runtimeArrivalDotCount);
                Vector3 dotPosition = end
                    + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                WriteQuad(
                    _arrivalSlotStart + i,
                    dotPosition,
                    coreSize * ArrivalDotRelativeScale,
                    dotColor);
            }

            UploadFrame();
        }

        private void RenderFailureFrame(Vector3 anchor, float size, float progress)
        {
            float recoil = Mathf.Sin(progress * Mathf.PI * 5f)
                * (1f - progress)
                * size
                * 0.22f;
            Vector3 corePosition = anchor + Vector3.right * recoil;
            float coreSize = size * Mathf.Lerp(1.05f, 0.08f, progress * progress);
            PrepareFrame(corePosition);

            float radius = Mathf.Sin(progress * Mathf.PI) * size * 1.25f;
            Color dotColor = _color;
            dotColor.a *= (1f - progress) * 0.72f;
            for (int i = 0; i < _runtimeFailureDotCount; i++)
            {
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, _runtimeFailureDotCount);
                Vector3 dotPosition = anchor
                    + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                WriteQuad(
                    _failureSlotStart + i,
                    dotPosition,
                    coreSize * FailureDotRelativeScale,
                    dotColor);
            }

            Color coreColor = _color;
            coreColor.a *= 1f - progress;
            WriteQuad(_coreSlot, corePosition, coreSize, coreColor);
            UploadFrame();
        }

        internal void PrepareForReuse()
        {
            CaptureDefaultColor();
            ResetVisuals();
            _color = _defaultColor;
            _visualScale = 1f;
        }

        internal void ResetForPool()
        {
            CaptureDefaultColor();
            ResetVisuals();
            _color = _defaultColor;
            _visualScale = 1f;
        }

        internal void WarmupForPool()
        {
            CaptureDefaultColor();
            EnsureRuntimeObjects();
            ResetForPool();
        }

        private void CaptureDefaultColor()
        {
            if (_hasDefaultColor)
            {
                return;
            }

            _defaultColor = _color;
            _hasDefaultColor = true;
        }

        private void EnsureRuntimeObjects()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
            }

            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _meshRenderer.sharedMaterial = SharedMaterial;
            BattleSorting.Apply(
                _meshRenderer,
                BattleSorting.Fx,
                BattleSorting.OrderFloatingText - 1);

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "RuntimeSweetTransferParticleBatch",
                    hideFlags = HideFlags.DontSave,
                };
                _mesh.MarkDynamic();
                _meshFilter.sharedMesh = _mesh;
            }
            else if (_meshFilter.sharedMesh != _mesh)
            {
                _meshFilter.sharedMesh = _mesh;
            }

            int afterimageCount = Mathf.Max(0, _afterimageCount);
            int arrivalCount = Mathf.Max(0, _arrivalDotCount);
            int failureCount = Mathf.Max(5, arrivalCount / 2);
            if (_quadCount == 0
                || afterimageCount != _runtimeAfterimageCount
                || arrivalCount != _runtimeArrivalDotCount
                || failureCount != _runtimeFailureDotCount)
            {
                BuildTopology(afterimageCount, arrivalCount, failureCount);
            }
        }

        private void BuildTopology(int afterimageCount, int arrivalCount, int failureCount)
        {
            _runtimeAfterimageCount = afterimageCount;
            _runtimeArrivalDotCount = arrivalCount;
            _runtimeFailureDotCount = failureCount;
            _afterimageSlotStart = 0;
            _failureSlotStart = _afterimageSlotStart + afterimageCount;
            _coreSlot = _failureSlotStart + failureCount;
            _arrivalSlotStart = _coreSlot + 1;
            _quadCount = afterimageCount + failureCount + 1 + arrivalCount;

            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _triangles.Clear();
            EnsureListCapacity(_vertices, _quadCount * 4);
            EnsureListCapacity(_colors, _quadCount * 4);
            EnsureListCapacity(_uvs, _quadCount * 4);
            EnsureListCapacity(_triangles, _quadCount * 6);

            for (int slot = 0; slot < _quadCount; slot++)
            {
                int vertexStart = _vertices.Count;
                _vertices.Add(Vector3.zero);
                _vertices.Add(Vector3.zero);
                _vertices.Add(Vector3.zero);
                _vertices.Add(Vector3.zero);
                _colors.Add(default);
                _colors.Add(default);
                _colors.Add(default);
                _colors.Add(default);
                _uvs.Add(new Vector2(0f, 0f));
                _uvs.Add(new Vector2(0f, 1f));
                _uvs.Add(new Vector2(1f, 1f));
                _uvs.Add(new Vector2(1f, 0f));
                _triangles.Add(vertexStart);
                _triangles.Add(vertexStart + 1);
                _triangles.Add(vertexStart + 2);
                _triangles.Add(vertexStart + 2);
                _triangles.Add(vertexStart + 3);
                _triangles.Add(vertexStart);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0, true);
            _mesh.RecalculateBounds();
        }

        private void PrepareFrame(Vector3 corePosition)
        {
            transform.position = corePosition;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            for (int i = 0; i < _colors.Count; i++)
            {
                _vertices[i] = Vector3.zero;
                _colors[i] = default;
            }
        }

        private void WriteQuad(int slot, Vector3 worldCenter, float localSize, Color color)
        {
            if (slot < 0 || slot >= _quadCount || color.a <= 0f || localSize <= 0f)
            {
                return;
            }

            int vertexStart = slot * 4;
            Vector3 center = transform.InverseTransformPoint(worldCenter);
            float halfSize = localSize * 0.5f;
            Vector3 right = Vector3.right * halfSize;
            Vector3 up = Vector3.up * halfSize;
            _vertices[vertexStart] = center - right - up;
            _vertices[vertexStart + 1] = center - right + up;
            _vertices[vertexStart + 2] = center + right + up;
            _vertices[vertexStart + 3] = center + right - up;
            Color32 color32 = color;
            _colors[vertexStart] = color32;
            _colors[vertexStart + 1] = color32;
            _colors[vertexStart + 2] = color32;
            _colors[vertexStart + 3] = color32;
        }

        private void UploadFrame()
        {
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.RecalculateBounds();
            _meshRenderer.enabled = true;
        }

        private void ResetVisuals()
        {
            _tween?.Kill(false);
            _tween = null;
            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = false;
            }
        }

        private static void Release(SweetTransferParticleView view, GameObjectPool pool)
        {
            if (view == null)
            {
                return;
            }

            if (pool != null)
            {
                pool.Release(view);
            }
            else if (Application.isPlaying)
            {
                Destroy(view.gameObject);
            }
            else
            {
                DestroyImmediate(view.gameObject);
            }
        }

        private static Vector3 Bezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float clamped = Mathf.Clamp01(t);
            float inverse = 1f - clamped;
            return inverse * inverse * start
                + 2f * inverse * clamped * control
                + clamped * clamped * end;
        }

        private void OnDestroy()
        {
            _tween?.Kill();
            _tween = null;

            Mesh ownedMesh = _mesh;
            _mesh = null;
            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = null;
            }

            if (ownedMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ownedMesh);
            }
            else
            {
                DestroyImmediate(ownedMesh);
            }
        }

        private static void EnsureListCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }
    }
}
