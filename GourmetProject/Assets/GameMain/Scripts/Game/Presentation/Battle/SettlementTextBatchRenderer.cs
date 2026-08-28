using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using GourmetProject.Game.UI.Common;

namespace GourmetProject.Game.Presentation.Battle
{
    internal enum SettlementTextAnimationKind
    {
        Stage = 0,
        Floating = 1,
    }

    internal readonly struct SettlementTextBatchHandle : IEquatable<SettlementTextBatchHandle>
    {
        public SettlementTextBatchHandle(int surfaceId, int slot, int generation)
        {
            SurfaceId = surfaceId;
            Slot = slot;
            Generation = generation;
        }

        public int SurfaceId { get; }
        public int Slot { get; }
        public int Generation { get; }
        public bool IsValid => SurfaceId > 0 && Slot >= 0 && Generation > 0;

        public bool Equals(SettlementTextBatchHandle other)
        {
            return SurfaceId == other.SurfaceId
                && Slot == other.Slot
                && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is SettlementTextBatchHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SurfaceId;
                hash = hash * 397 ^ Slot;
                hash = hash * 397 ^ Generation;
                return hash;
            }
        }
    }

    internal readonly struct SettlementTextAnimationSample
    {
        public SettlementTextAnimationSample(float scale, float verticalOffset, float alpha)
        {
            Scale = scale;
            VerticalOffset = verticalOffset;
            Alpha = alpha;
        }

        public float Scale { get; }
        public float VerticalOffset { get; }
        public float Alpha { get; }
    }

    /// <summary>
    /// 结算世界文字的统一动态网格。TMP 仅作为隐藏排版模板；可见顶点由 Burst Job
    /// 并行更新后一次性上传，不再为每条提示创建 Transform、Renderer 或 Tween。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class SettlementTextBatchRenderer : MonoBehaviour
    {
        internal const int InitialLabelCapacity = 64;
        internal const int InitialGlyphCapacity = 2048;

        private const int VerticesPerGlyph = 4;
        internal const int InitialVertexCapacity =
            InitialGlyphCapacity * VerticesPerGlyph + InitialLabelCapacity * 4;
        private const int InitialIndexCapacity =
            InitialGlyphCapacity * 6 + InitialLabelCapacity * 6;
        private const int JobBatchSize = 64;

        private static readonly ProfilerMarker RebuildMarker =
            new("Gourmet.SettlementText.Rebuild");
        private static readonly ProfilerMarker JobMarker =
            new("Gourmet.SettlementText.Job");
        private static readonly ProfilerMarker UploadMarker =
            new("Gourmet.SettlementText.Upload");

        private static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, 1),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 1),
        };

        private readonly Dictionary<Transform, BatchSurface> _surfacesByParent = new();
        private readonly Dictionary<int, BatchSurface> _surfacesById = new();
        private readonly List<BatchSurface> _surfaceScratch = new();
        private readonly List<Material> _ownedMaterials = new();

        private FloatingTextView _effectTemplate;
        private SettlementStageLabelView _stageTemplate;
        private SettlementStageLabelView _finaleTemplate;
        private FloatingTextView _effectTemplateSource;
        private SettlementStageLabelView _stageTemplateSource;
        private SettlementStageLabelView _finaleTemplateSource;
        private Material _effectBackgroundMaterial;
        private int _nextSurfaceId;
        private bool _initialized;
        private bool _disposed;

        internal int ActiveLabelCount
        {
            get
            {
                int count = 0;
                foreach (BatchSurface surface in _surfacesById.Values)
                {
                    count += surface.ActiveLabelCount;
                }

                return count;
            }
        }

        internal int SurfaceCount => _surfacesById.Count;

        internal int BatchRendererCount
        {
            get
            {
                int count = 0;
                foreach (BatchSurface surface in _surfacesById.Values)
                {
                    if (surface.Renderer != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal int VisibleVertexCount
        {
            get
            {
                int count = 0;
                foreach (BatchSurface surface in _surfacesById.Values)
                {
                    count += surface.VisibleVertexCount;
                }

                return count;
            }
        }

        internal int MaterialGroupCount
        {
            get
            {
                int count = 0;
                foreach (BatchSurface surface in _surfacesById.Values)
                {
                    count += surface.MaterialGroupCount;
                }

                return count;
            }
        }

        internal int VertexCapacity
        {
            get
            {
                int capacity = 0;
                foreach (BatchSurface surface in _surfacesById.Values)
                {
                    capacity += surface.VertexCapacity;
                }

                return capacity;
            }
        }

        internal Mesh GetSurfaceMeshForTests(Transform parent)
        {
            return parent != null
                && _surfacesByParent.TryGetValue(parent, out BatchSurface surface)
                    ? surface.Mesh
                    : null;
        }

        internal MeshRenderer GetSurfaceRendererForTests(Transform parent)
        {
            return parent != null
                && _surfacesByParent.TryGetValue(parent, out BatchSurface surface)
                    ? surface.Renderer
                    : null;
        }

        internal bool Initialize(
            FloatingTextView effectTemplate,
            SettlementStageLabelView stageTemplate,
            SettlementStageLabelView finaleTemplate)
        {
            if (_disposed)
            {
                return false;
            }

            bool unchanged = _initialized
                && _effectTemplateSource == effectTemplate
                && _stageTemplateSource == stageTemplate
                && _finaleTemplateSource == finaleTemplate;
            if (unchanged)
            {
                return true;
            }

            if (!ValidateSourceTemplates(effectTemplate, stageTemplate, finaleTemplate))
            {
                ClearAll();
                DestroyTemplates();
                _effectTemplateSource = null;
                _stageTemplateSource = null;
                _finaleTemplateSource = null;
                _initialized = false;
                return false;
            }

            ClearAll();
            DestroyTemplates();
            _effectTemplateSource = effectTemplate;
            _stageTemplateSource = stageTemplate;
            _finaleTemplateSource = finaleTemplate;
            _effectTemplate = CreateHiddenTemplate(effectTemplate, "Settlement Effect Layout Template");
            _stageTemplate = CreateHiddenTemplate(stageTemplate, "Settlement Stage Layout Template");
            _finaleTemplate = CreateHiddenTemplate(finaleTemplate, "Settlement Finale Layout Template");
            PrepareBackgroundMaterial();
            DisableTemplateRenderers();
            _initialized = true;
            return true;
        }

        internal SettlementTextBatchHandle SpawnStage(
            Transform parent,
            Vector3 worldAnchor,
            string header,
            string body,
            Color theme,
            bool finalStamp,
            float duration,
            float visualScale,
            bool holdUntilCleared,
            Color? headerSemanticColor,
            int sortingOrder,
            float verticalDriftDirection,
            float impactScale)
        {
            SettlementStageLabelView template = finalStamp ? _finaleTemplate : _stageTemplate;
            if (!ValidateTemplate(template, finalStamp ? "最终总分" : "舞台"))
            {
                return default;
            }

            LabelGeometry geometry = BakeStageGeometry(
                template,
                header,
                body,
                theme,
                headerSemanticColor,
                sortingOrder);
            return AddLabel(
                parent,
                worldAnchor,
                geometry,
                SettlementTextAnimationKind.Stage,
                duration,
                Mathf.Max(0.0001f, visualScale) * Mathf.Max(0.01f, impactScale),
                0.10f * (Mathf.Approximately(verticalDriftDirection, 0f)
                    ? 0f
                    : verticalDriftDirection < 0f ? -1f : 1f),
                holdUntilCleared,
                autoRelease: false,
                startDelay: 0f,
                sortingOrder);
        }

        internal SettlementTextBatchHandle SpawnFloating(
            Transform parent,
            Vector3 worldAnchor,
            string sourceName,
            string effectText,
            Color? effectColor,
            float rise,
            float duration,
            float delay,
            float visualScale,
            int sortingOrder)
        {
            if (!ValidateTemplate(_effectTemplate, "临时效果"))
            {
                return default;
            }

            LabelGeometry geometry = BakeFloatingGeometry(
                sourceName,
                effectText,
                effectColor);
            return AddLabel(
                parent,
                worldAnchor,
                geometry,
                SettlementTextAnimationKind.Floating,
                duration,
                Mathf.Max(0.0001f, visualScale),
                rise,
                holdUntilCleared: false,
                autoRelease: true,
                startDelay: delay,
                sortingOrder);
        }

        internal bool IsAlive(SettlementTextBatchHandle handle)
        {
            return handle.IsValid
                && _surfacesById.TryGetValue(handle.SurfaceId, out BatchSurface surface)
                && surface.IsAlive(handle);
        }

        internal void Release(SettlementTextBatchHandle handle)
        {
            if (!handle.IsValid
                || !_surfacesById.TryGetValue(handle.SurfaceId, out BatchSurface surface))
            {
                return;
            }

            surface.Release(handle);
        }

        internal void ClearAll()
        {
            foreach (BatchSurface surface in _surfacesById.Values)
            {
                surface.Clear();
            }
        }

        internal void AdvanceForTests(float now)
        {
            UpdateSurfaces(now);
            CompleteAndUploadSurfaces();
        }

        internal static SettlementTextAnimationSample EvaluateAnimation(
            SettlementTextAnimationKind kind,
            float now,
            float startTime,
            float duration,
            float targetScale,
            float rise,
            bool hold)
        {
            float safeDuration = math.max(0.0001f, duration);
            float rawProgress = (now - startTime) / safeDuration;
            float progress = math.saturate(rawProgress);
            if (kind == SettlementTextAnimationKind.Floating)
            {
                return new SettlementTextAnimationSample(
                    targetScale,
                    rise * progress * targetScale,
                    rawProgress < 0f ? 0f : 1f - progress);
            }

            float enter = SmoothStep(math.saturate(progress / 0.28f));
            float pulse = math.sin(math.saturate(progress / 0.48f) * math.PI) * 0.08f;
            float scale = targetScale * (0.80f + enter * 0.20f + pulse);
            float alpha = hold ? 1f : math.saturate((1f - progress) / 0.24f);
            return new SettlementTextAnimationSample(
                scale,
                rise * targetScale * enter,
                alpha);
        }

        private void Update()
        {
            UpdateSurfaces(Time.time);
        }

        private void LateUpdate()
        {
            CompleteAndUploadSurfaces();
        }

        private void OnDisable()
        {
            CompleteAndUploadSurfaces();
            ClearAll();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private void UpdateSurfaces(float now)
        {
            if (_disposed)
            {
                return;
            }

            CollectSurfaces();
            for (int i = 0; i < _surfaceScratch.Count; i++)
            {
                BatchSurface surface = _surfaceScratch[i];
                if (surface.Parent == null)
                {
                    RemoveAndDisposeSurface(surface);
                    continue;
                }

                surface.Schedule(now);
            }
        }

        private void CompleteAndUploadSurfaces()
        {
            if (_disposed)
            {
                return;
            }

            CollectSurfaces();
            for (int i = 0; i < _surfaceScratch.Count; i++)
            {
                _surfaceScratch[i].CompleteAndUpload();
            }
        }

        private SettlementTextBatchHandle AddLabel(
            Transform parent,
            Vector3 worldAnchor,
            LabelGeometry geometry,
            SettlementTextAnimationKind kind,
            float duration,
            float visualScale,
            float rise,
            bool holdUntilCleared,
            bool autoRelease,
            float startDelay,
            int sortingOrder)
        {
            if (geometry == null || geometry.VertexCount == 0)
            {
                return default;
            }

            BatchSurface surface = GetOrCreateSurface(parent != null ? parent : transform);
            var record = new LabelRecord(
                geometry,
                kind,
                surface.Root.InverseTransformPoint(worldAnchor),
                Time.time + Mathf.Max(0f, startDelay),
                Mathf.Max(0.0001f, duration),
                visualScale,
                rise,
                holdUntilCleared,
                autoRelease,
                sortingOrder >= 0 ? sortingOrder : WorldLabelSorting.NextOrder());
            return surface.Add(record);
        }

        private BatchSurface GetOrCreateSurface(Transform parent)
        {
            if (_surfacesByParent.TryGetValue(parent, out BatchSurface existing))
            {
                return existing;
            }

            int id = ++_nextSurfaceId;
            if (id <= 0)
            {
                _nextSurfaceId = id = 1;
            }

            var surface = new BatchSurface(id, parent);
            _surfacesByParent.Add(parent, surface);
            _surfacesById.Add(id, surface);
            return surface;
        }

        private LabelGeometry BakeStageGeometry(
            SettlementStageLabelView template,
            string header,
            string body,
            Color theme,
            Color? headerSemanticColor,
            int sortingOrder)
        {
            template.PrepareForReuse();
            template.Bind(header, body, theme, headerSemanticColor, sortingOrder);
            var geometry = new LabelGeometry();
            AppendTextGeometry(geometry, template.transform, template.HeaderText, renderLayer: 1);
            AppendTextGeometry(geometry, template.transform, template.BodyText, renderLayer: 1);
            DisableRenderers(template.gameObject);
            return geometry;
        }

        private LabelGeometry BakeFloatingGeometry(
            string sourceName,
            string effectText,
            Color? effectColor)
        {
            _effectTemplate.BindForBatch(sourceName, effectText, effectColor);
            var geometry = new LabelGeometry();
            AppendSpriteGeometry(
                geometry,
                _effectTemplate.transform,
                _effectTemplate.Background,
                _effectBackgroundMaterial,
                renderLayer: 0);
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                AppendTextGeometry(
                    geometry,
                    _effectTemplate.transform,
                    _effectTemplate.SourceText,
                    renderLayer: 1);
            }

            AppendTextGeometry(
                geometry,
                _effectTemplate.transform,
                _effectTemplate.EffectText,
                renderLayer: 1);
            DisableRenderers(_effectTemplate.gameObject);
            return geometry;
        }

        private static void AppendTextGeometry(
            LabelGeometry destination,
            Transform templateRoot,
            TextMeshPro text,
            int renderLayer)
        {
            if (text == null)
            {
                return;
            }

            text.ForceMeshUpdate(true, true);
            Matrix4x4 relative = templateRoot.worldToLocalMatrix * text.transform.localToWorldMatrix;
            TMP_MeshInfo[] meshInfos = text.textInfo?.meshInfo;
            if (meshInfos == null)
            {
                return;
            }

            for (int meshIndex = 0; meshIndex < meshInfos.Length; meshIndex++)
            {
                TMP_MeshInfo meshInfo = meshInfos[meshIndex];
                int vertexCount = meshInfo.vertexCount;
                if (vertexCount <= 0 || meshInfo.material == null)
                {
                    continue;
                }

                var vertices = new SourceVertex[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 normal = relative.MultiplyVector(meshInfo.normals[i]).normalized;
                    Vector4 sourceTangent = meshInfo.tangents[i];
                    Vector3 tangentDirection = relative.MultiplyVector(
                        new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z)).normalized;
                    vertices[i] = new SourceVertex(
                        relative.MultiplyPoint3x4(meshInfo.vertices[i]),
                        normal,
                        new Vector4(
                            tangentDirection.x,
                            tangentDirection.y,
                            tangentDirection.z,
                            sourceTangent.w),
                        meshInfo.uvs0[i],
                        meshInfo.uvs2[i],
                        meshInfo.colors32[i]);
                }

                int indexCount = vertexCount / 4 * 6;
                var triangles = new int[indexCount];
                Array.Copy(meshInfo.triangles, triangles, indexCount);
                destination.AddPart(new GeometryPart(
                    meshInfo.material,
                    vertices,
                    triangles,
                    renderLayer));
            }
        }

        private static void AppendSpriteGeometry(
            LabelGeometry destination,
            Transform templateRoot,
            SpriteRenderer renderer,
            Material material,
            int renderLayer)
        {
            Sprite sprite = renderer != null ? renderer.sprite : null;
            if (sprite == null || material == null)
            {
                return;
            }

            Matrix4x4 relative = templateRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector2[] spriteVertices = sprite.vertices;
            Vector2[] spriteUv = sprite.uv;
            var vertices = new SourceVertex[spriteVertices.Length];
            Color32 color = renderer.color;
            for (int i = 0; i < spriteVertices.Length; i++)
            {
                Vector2 position = spriteVertices[i];
                Vector2 uv = spriteUv[i];
                vertices[i] = new SourceVertex(
                    relative.MultiplyPoint3x4(new Vector3(position.x, position.y, 0f)),
                    Vector3.back,
                    new Vector4(-1f, 0f, 0f, 1f),
                    new Vector4(uv.x, uv.y, 0f, 0f),
                    Vector2.zero,
                    color);
            }

            ushort[] sourceTriangles = sprite.triangles;
            var triangles = new int[sourceTriangles.Length];
            for (int i = 0; i < sourceTriangles.Length; i++)
            {
                triangles[i] = sourceTriangles[i];
            }

            destination.AddPart(new GeometryPart(material, vertices, triangles, renderLayer));
        }

        private void PrepareBackgroundMaterial()
        {
            SpriteRenderer renderer = _effectTemplate != null ? _effectTemplate.Background : null;
            if (renderer == null || renderer.sprite == null)
            {
                return;
            }

            Material source = renderer.sharedMaterial;
            Shader fallback = Shader.Find("Sprites/Default");
            if (source == null && fallback == null)
            {
                return;
            }

            _effectBackgroundMaterial = source != null
                ? new Material(source)
                : new Material(fallback);
            _effectBackgroundMaterial.name = "Settlement Effect Background (Batch)";
            _effectBackgroundMaterial.hideFlags = HideFlags.HideAndDontSave;
            _effectBackgroundMaterial.mainTexture = renderer.sprite.texture;
            _ownedMaterials.Add(_effectBackgroundMaterial);
        }

        private bool ValidateTemplate(Component template, string label)
        {
            if (_disposed)
            {
                return false;
            }

            if (!_initialized)
            {
                Debug.LogError($"{nameof(SettlementTextBatchRenderer)} 尚未初始化。", this);
                return false;
            }

            if (template != null)
            {
                return true;
            }

            Debug.LogError($"{nameof(SettlementTextBatchRenderer)} 缺少{label}文字模板。", this);
            return false;
        }

        private void DisableTemplateRenderers()
        {
            if (_effectTemplate != null)
            {
                DisableRenderers(_effectTemplate.gameObject);
            }

            if (_stageTemplate != null)
            {
                DisableRenderers(_stageTemplate.gameObject);
            }

            if (_finaleTemplate != null)
            {
                DisableRenderers(_finaleTemplate.gameObject);
            }
        }

        private static void DisableRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }
        }

        private T CreateHiddenTemplate<T>(T source, string name) where T : Component
        {
            if (source == null)
            {
                return null;
            }

            T instance = Instantiate(source, transform, false);
            instance.name = name;
            instance.gameObject.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = source.transform.localScale;
            return instance;
        }

        private bool ValidateSourceTemplates(
            FloatingTextView effectTemplate,
            SettlementStageLabelView stageTemplate,
            SettlementStageLabelView finaleTemplate)
        {
            bool valid = true;
            if (effectTemplate == null
                || effectTemplate.Background == null
                || effectTemplate.Background.sprite == null
                || effectTemplate.SourceText == null
                || effectTemplate.EffectText == null)
            {
                Debug.LogError(
                    $"{nameof(SettlementTextBatchRenderer)} 的临时效果条模板配置不完整。",
                    this);
                valid = false;
            }

            if (stageTemplate == null
                || stageTemplate.HeaderText == null
                || stageTemplate.BodyText == null)
            {
                Debug.LogError(
                    $"{nameof(SettlementTextBatchRenderer)} 的舞台文字模板配置不完整。",
                    this);
                valid = false;
            }

            if (finaleTemplate == null
                || finaleTemplate.HeaderText == null
                || finaleTemplate.BodyText == null)
            {
                Debug.LogError(
                    $"{nameof(SettlementTextBatchRenderer)} 的最终总分模板配置不完整。",
                    this);
                valid = false;
            }

            return valid;
        }

        private void CollectSurfaces()
        {
            _surfaceScratch.Clear();
            foreach (BatchSurface surface in _surfacesById.Values)
            {
                _surfaceScratch.Add(surface);
            }
        }

        private void RemoveAndDisposeSurface(BatchSurface surface)
        {
            if (surface == null)
            {
                return;
            }

            _surfacesById.Remove(surface.Id);
            Transform staleParent = null;
            foreach (KeyValuePair<Transform, BatchSurface> entry in _surfacesByParent)
            {
                if (ReferenceEquals(entry.Value, surface))
                {
                    staleParent = entry.Key;
                    break;
                }
            }

            if (!ReferenceEquals(staleParent, null))
            {
                _surfacesByParent.Remove(staleParent);
            }

            surface.Dispose();
        }

        private void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (BatchSurface surface in _surfacesById.Values)
            {
                surface.Dispose();
            }

            _surfacesById.Clear();
            _surfacesByParent.Clear();
            DestroyTemplates();
        }

        private void DestroyTemplates()
        {
            DestroyUnityObject(_effectTemplate != null ? _effectTemplate.gameObject : null);
            DestroyUnityObject(_stageTemplate != null ? _stageTemplate.gameObject : null);
            DestroyUnityObject(_finaleTemplate != null ? _finaleTemplate.gameObject : null);
            _effectTemplate = null;
            _stageTemplate = null;
            _finaleTemplate = null;

            for (int i = 0; i < _ownedMaterials.Count; i++)
            {
                DestroyUnityObject(_ownedMaterials[i]);
            }

            _ownedMaterials.Clear();
            _effectBackgroundMaterial = null;
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private static uint PackColor(Color32 color)
        {
            return color.r
                | (uint)color.g << 8
                | (uint)color.b << 16
                | (uint)color.a << 24;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct DynamicVertex
        {
            public DynamicVertex(float3 position, uint color)
            {
                Position = position;
                Color = color;
            }

            public readonly float3 Position;
            public readonly uint Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct StaticVertex
        {
            public StaticVertex(
                float3 normal,
                float4 tangent,
                float4 uv0,
                float2 uv2)
            {
                Normal = normal;
                Tangent = tangent;
                Uv0 = uv0;
                Uv2 = uv2;
            }

            public readonly float3 Normal;
            public readonly float4 Tangent;
            public readonly float4 Uv0;
            public readonly float2 Uv2;
        }

        private readonly struct BaseAnimatedVertex
        {
            public BaseAnimatedVertex(float3 position, uint color, int labelIndex)
            {
                Position = position;
                Color = color;
                LabelIndex = labelIndex;
            }

            public readonly float3 Position;
            public readonly uint Color;
            public readonly int LabelIndex;
        }

        private readonly struct LabelAnimationState
        {
            public LabelAnimationState(LabelRecord record)
            {
                Anchor = record.Anchor;
                StartTime = record.StartTime;
                Duration = record.Duration;
                TargetScale = record.VisualScale;
                Rise = record.Rise;
                Kind = (int)record.Kind;
                Hold = record.HoldUntilCleared ? 1 : 0;
            }

            public readonly float3 Anchor;
            public readonly float StartTime;
            public readonly float Duration;
            public readonly float TargetScale;
            public readonly float Rise;
            public readonly int Kind;
            public readonly int Hold;
        }

        [BurstCompile]
        private struct AnimateVerticesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<BaseAnimatedVertex> BaseVertices;
            [ReadOnly] public NativeArray<LabelAnimationState> States;
            [WriteOnly] public NativeArray<DynamicVertex> Output;
            public float Now;

            public void Execute(int index)
            {
                BaseAnimatedVertex source = BaseVertices[index];
                LabelAnimationState state = States[source.LabelIndex];
                float rawProgress = (Now - state.StartTime) / math.max(0.0001f, state.Duration);
                float progress = math.saturate(rawProgress);
                float scale;
                float verticalOffset;
                float alpha;

                if (state.Kind == (int)SettlementTextAnimationKind.Floating)
                {
                    scale = state.TargetScale;
                    verticalOffset = state.Rise * progress * state.TargetScale;
                    alpha = rawProgress < 0f ? 0f : 1f - progress;
                }
                else
                {
                    float enterInput = math.saturate(progress / 0.28f);
                    float enter = enterInput * enterInput * (3f - 2f * enterInput);
                    float pulse = math.sin(math.saturate(progress / 0.48f) * math.PI) * 0.08f;
                    scale = state.TargetScale * (0.80f + enter * 0.20f + pulse);
                    verticalOffset = state.Rise * state.TargetScale * enter;
                    alpha = state.Hold != 0
                        ? 1f
                        : math.saturate((1f - progress) / 0.24f);
                }

                float3 position = new(
                    state.Anchor.x + source.Position.x * scale,
                    state.Anchor.y + source.Position.y * scale + verticalOffset,
                    state.Anchor.z + source.Position.z);
                uint sourceAlpha = source.Color >> 24;
                uint outputAlpha = (uint)math.round(sourceAlpha * math.saturate(alpha));
                uint color = (source.Color & 0x00FFFFFFu) | outputAlpha << 24;
                Output[index] = new DynamicVertex(position, color);
            }
        }

        private sealed class BatchSurface : IDisposable
        {
            private readonly List<LabelSlot> _slots = new(InitialLabelCapacity);
            private readonly List<int> _activeSlots = new(InitialLabelCapacity);
            private readonly List<int> _sortedSlots = new(InitialLabelCapacity);
            private readonly Stack<int> _freeSlots = new(InitialLabelCapacity);
            private readonly Dictionary<Material, MaterialGroup> _materialGroups = new();
            private readonly List<MaterialGroup> _sortedMaterialGroups = new();
            private readonly NativeList<BaseAnimatedVertex> _baseVertices;
            private readonly NativeList<StaticVertex> _staticVertices;
            private readonly NativeList<DynamicVertex> _dynamicVertices;
            private readonly NativeList<LabelAnimationState> _states;
            private readonly Mesh _mesh;
            private readonly MeshFilter _filter;

            private JobHandle _jobHandle;
            private bool _jobScheduled;
            private bool _structureDirty = true;
            private bool _needsEvaluation = true;
            private bool _disposed;
            private float _lastEvaluatedTime = float.NaN;
            private int _vertexBufferCapacity;
            private int _indexBufferCapacity;
            private int _visibleVertexCount;

            public BatchSurface(int id, Transform parent)
            {
                Id = id;
                Parent = parent;
                var root = new GameObject($"Settlement Text Batch {id}");
                root.hideFlags = HideFlags.DontSave;
                Root = root.transform;
                Root.SetParent(parent, false);
                Root.localPosition = Vector3.zero;
                Root.localRotation = Quaternion.identity;
                Root.localScale = Vector3.one;
                _filter = root.AddComponent<MeshFilter>();
                Renderer = root.AddComponent<MeshRenderer>();
                BattleSorting.Apply(Renderer, BattleSorting.Fx, BattleSorting.OrderFloatingText + 2);
                Renderer.shadowCastingMode = ShadowCastingMode.Off;
                Renderer.receiveShadows = false;
                Renderer.lightProbeUsage = LightProbeUsage.Off;
                Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                Renderer.enabled = false;

                _mesh = new Mesh
                {
                    name = $"Settlement Text Batch Mesh {id}",
                    hideFlags = HideFlags.DontSave,
                    indexFormat = IndexFormat.UInt32,
                };
                _mesh.MarkDynamic();
                _filter.sharedMesh = _mesh;
                _baseVertices = new NativeList<BaseAnimatedVertex>(InitialVertexCapacity, Allocator.Persistent);
                _staticVertices = new NativeList<StaticVertex>(InitialVertexCapacity, Allocator.Persistent);
                _dynamicVertices = new NativeList<DynamicVertex>(InitialVertexCapacity, Allocator.Persistent);
                _states = new NativeList<LabelAnimationState>(InitialLabelCapacity, Allocator.Persistent);
            }

            public int Id { get; }
            public Transform Parent { get; }
            public Transform Root { get; }
            public Mesh Mesh => _mesh;
            public MeshRenderer Renderer { get; }
            public int ActiveLabelCount => _activeSlots.Count;
            public int VisibleVertexCount => _visibleVertexCount;
            public int MaterialGroupCount => _sortedMaterialGroups.Count;
            public int VertexCapacity => _vertexBufferCapacity;

            public SettlementTextBatchHandle Add(LabelRecord record)
            {
                int slotIndex;
                LabelSlot slot;
                if (_freeSlots.Count > 0)
                {
                    slotIndex = _freeSlots.Pop();
                    slot = _slots[slotIndex];
                    slot.Generation++;
                    if (slot.Generation <= 0)
                    {
                        slot.Generation = 1;
                    }
                }
                else
                {
                    slotIndex = _slots.Count;
                    slot = new LabelSlot { Generation = 1 };
                    _slots.Add(slot);
                }

                slot.Record = record;
                slot.ActiveIndex = _activeSlots.Count;
                _activeSlots.Add(slotIndex);
                _structureDirty = true;
                _needsEvaluation = true;
                return new SettlementTextBatchHandle(Id, slotIndex, slot.Generation);
            }

            public bool IsAlive(SettlementTextBatchHandle handle)
            {
                return handle.SurfaceId == Id
                    && handle.Slot >= 0
                    && handle.Slot < _slots.Count
                    && _slots[handle.Slot].Record != null
                    && _slots[handle.Slot].Generation == handle.Generation;
            }

            public void Release(SettlementTextBatchHandle handle)
            {
                if (!IsAlive(handle))
                {
                    return;
                }

                ReleaseSlot(handle.Slot);
            }

            public void Clear()
            {
                CompleteJob();
                while (_activeSlots.Count > 0)
                {
                    ReleaseSlot(_activeSlots[_activeSlots.Count - 1]);
                }

                RebuildIfNeeded();
            }

            public void Schedule(float now)
            {
                if (_disposed)
                {
                    return;
                }

                CompleteJob();
                ExpireAutomaticLabels(now);
                RebuildIfNeeded();
                if (_visibleVertexCount == 0)
                {
                    return;
                }

                if (!_needsEvaluation && Mathf.Approximately(now, _lastEvaluatedTime))
                {
                    return;
                }

                using (JobMarker.Auto())
                {
                    var job = new AnimateVerticesJob
                    {
                        BaseVertices = _baseVertices.AsArray(),
                        States = _states.AsArray(),
                        Output = _dynamicVertices.AsArray(),
                        Now = now,
                    };
                    _jobHandle = job.Schedule(_visibleVertexCount, JobBatchSize);
                    _jobScheduled = true;
                }

                _lastEvaluatedTime = now;
                _needsEvaluation = false;
            }

            public void CompleteAndUpload()
            {
                if (!_jobScheduled || _disposed)
                {
                    return;
                }

                CompleteJob();
                if (_visibleVertexCount == 0)
                {
                    return;
                }

                using (UploadMarker.Auto())
                {
                    _mesh.SetVertexBufferData(
                        _dynamicVertices.AsArray(),
                        0,
                        0,
                        _visibleVertexCount,
                        0,
                        MeshUpdateFlags.DontRecalculateBounds
                        | MeshUpdateFlags.DontValidateIndices
                        | MeshUpdateFlags.DontNotifyMeshUsers);
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                CompleteJob();
                if (_baseVertices.IsCreated) _baseVertices.Dispose();
                if (_staticVertices.IsCreated) _staticVertices.Dispose();
                if (_dynamicVertices.IsCreated) _dynamicVertices.Dispose();
                if (_states.IsCreated) _states.Dispose();
                DestroyUnityObject(_mesh);
                DestroyUnityObject(Root != null ? Root.gameObject : null);
            }

            private void ExpireAutomaticLabels(float now)
            {
                for (int activeIndex = _activeSlots.Count - 1; activeIndex >= 0; activeIndex--)
                {
                    int slotIndex = _activeSlots[activeIndex];
                    LabelRecord record = _slots[slotIndex].Record;
                    if (record != null
                        && record.AutoRelease
                        && now >= record.StartTime + record.Duration)
                    {
                        ReleaseSlot(slotIndex);
                    }
                }
            }

            private void ReleaseSlot(int slotIndex)
            {
                LabelSlot slot = _slots[slotIndex];
                if (slot.Record == null)
                {
                    return;
                }

                int activeIndex = slot.ActiveIndex;
                int lastActiveIndex = _activeSlots.Count - 1;
                int movedSlotIndex = _activeSlots[lastActiveIndex];
                _activeSlots[activeIndex] = movedSlotIndex;
                _activeSlots.RemoveAt(lastActiveIndex);
                if (movedSlotIndex != slotIndex)
                {
                    _slots[movedSlotIndex].ActiveIndex = activeIndex;
                }

                slot.Record = null;
                slot.ActiveIndex = -1;
                _freeSlots.Push(slotIndex);
                _structureDirty = true;
                _needsEvaluation = true;
            }

            private void RebuildIfNeeded()
            {
                if (!_structureDirty)
                {
                    return;
                }

                using (RebuildMarker.Auto())
                {
                    CompleteJob();
                    _baseVertices.Clear();
                    _staticVertices.Clear();
                    _dynamicVertices.Clear();
                    _states.Clear();
                    _materialGroups.Clear();
                    _sortedMaterialGroups.Clear();
                    _sortedSlots.Clear();
                    _sortedSlots.AddRange(_activeSlots);
                    _sortedSlots.Sort(CompareSlots);

                    Bounds bounds = default;
                    bool hasBounds = false;
                    int highestSortingOrder = BattleSorting.OrderFloatingText;
                    for (int sortedIndex = 0; sortedIndex < _sortedSlots.Count; sortedIndex++)
                    {
                        LabelRecord record = _slots[_sortedSlots[sortedIndex]].Record;
                        if (record == null)
                        {
                            continue;
                        }

                        int stateIndex = _states.Length;
                        _states.Add(new LabelAnimationState(record));
                        AppendRecord(record, stateIndex, ref bounds, ref hasBounds);
                        highestSortingOrder = Mathf.Max(highestSortingOrder, record.SortingOrder);
                    }

                    _visibleVertexCount = _baseVertices.Length;
                    _dynamicVertices.ResizeUninitialized(_visibleVertexCount);
                    BuildMesh(bounds, hasBounds);
                    Renderer.sortingOrder = highestSortingOrder + 2;
                    _structureDirty = false;
                    _needsEvaluation = _visibleVertexCount > 0;
                }
            }

            private int CompareSlots(int leftSlot, int rightSlot)
            {
                LabelRecord left = _slots[leftSlot].Record;
                LabelRecord right = _slots[rightSlot].Record;
                int order = left.SortingOrder.CompareTo(right.SortingOrder);
                return order != 0 ? order : leftSlot.CompareTo(rightSlot);
            }

            private void AppendRecord(
                LabelRecord record,
                int stateIndex,
                ref Bounds bounds,
                ref bool hasBounds)
            {
                for (int partIndex = 0; partIndex < record.Geometry.Parts.Count; partIndex++)
                {
                    GeometryPart part = record.Geometry.Parts[partIndex];
                    if (part.Material == null || part.Vertices.Length == 0)
                    {
                        continue;
                    }

                    if (!_materialGroups.TryGetValue(part.Material, out MaterialGroup group))
                    {
                        group = new MaterialGroup(part.Material, part.RenderLayer, _materialGroups.Count);
                        _materialGroups.Add(part.Material, group);
                        _sortedMaterialGroups.Add(group);
                    }

                    int vertexOffset = _baseVertices.Length;
                    for (int vertexIndex = 0; vertexIndex < part.Vertices.Length; vertexIndex++)
                    {
                        SourceVertex source = part.Vertices[vertexIndex];
                        _baseVertices.Add(new BaseAnimatedVertex(
                            source.Position,
                            PackColor(source.Color),
                            stateIndex));
                        _staticVertices.Add(new StaticVertex(
                            source.Normal,
                            source.Tangent,
                            source.Uv0,
                            source.Uv2));
                        EncapsulateAnimatedBounds(record, source.Position, ref bounds, ref hasBounds);
                    }

                    for (int triangleIndex = 0; triangleIndex < part.Triangles.Length; triangleIndex++)
                    {
                        group.Indices.Add(vertexOffset + part.Triangles[triangleIndex]);
                    }
                }
            }

            private static void EncapsulateAnimatedBounds(
                LabelRecord record,
                Vector3 localPosition,
                ref Bounds bounds,
                ref bool hasBounds)
            {
                float maxScale = record.Kind == SettlementTextAnimationKind.Stage
                    ? record.VisualScale * 1.08f
                    : record.VisualScale;
                Vector3 basePosition = new(
                    record.Anchor.x + localPosition.x * maxScale,
                    record.Anchor.y + localPosition.y * maxScale,
                    record.Anchor.z + localPosition.z);
                Vector3 endPosition = basePosition
                    + Vector3.up * (record.Rise * record.VisualScale);
                if (!hasBounds)
                {
                    bounds = new Bounds(basePosition, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(basePosition);
                }

                bounds.Encapsulate(endPosition);
            }

            private void BuildMesh(Bounds bounds, bool hasBounds)
            {
                _sortedMaterialGroups.Sort(MaterialGroup.Compare);
                int indexCount = 0;
                for (int i = 0; i < _sortedMaterialGroups.Count; i++)
                {
                    indexCount += _sortedMaterialGroups[i].Indices.Count;
                }

                if (_visibleVertexCount == 0 || indexCount == 0)
                {
                    _mesh.subMeshCount = 0;
                    // Unity 会在移除最后一个 SubMesh 时释放索引缓冲区；同步清空缓存容量，
                    // 这样下次重新出现文字时 EnsureIndexBufferCapacity 会重新创建缓冲区。
                    _indexBufferCapacity = 0;
                    Renderer.sharedMaterials = Array.Empty<Material>();
                    Renderer.enabled = false;
                    return;
                }

                EnsureVertexBufferCapacity(_visibleVertexCount);
                EnsureIndexBufferCapacity(indexCount);
                _mesh.SetVertexBufferData(
                    _staticVertices.AsArray(),
                    0,
                    0,
                    _visibleVertexCount,
                    1,
                    MeshUpdateFlags.DontRecalculateBounds
                    | MeshUpdateFlags.DontValidateIndices
                    | MeshUpdateFlags.DontNotifyMeshUsers);

                var indices = new int[indexCount];
                var materials = new Material[_sortedMaterialGroups.Count];
                int indexOffset = 0;
                for (int groupIndex = 0; groupIndex < _sortedMaterialGroups.Count; groupIndex++)
                {
                    MaterialGroup group = _sortedMaterialGroups[groupIndex];
                    group.Indices.CopyTo(indices, indexOffset);
                    materials[groupIndex] = group.Material;
                    indexOffset += group.Indices.Count;
                }

                _mesh.SetIndexBufferData(
                    indices,
                    0,
                    0,
                    indexCount,
                    MeshUpdateFlags.DontRecalculateBounds
                    | MeshUpdateFlags.DontValidateIndices
                    | MeshUpdateFlags.DontNotifyMeshUsers);
                Bounds meshBounds = hasBounds
                    ? bounds
                    : new Bounds(Vector3.zero, Vector3.one);
                meshBounds.Expand(0.10f);
                // 写入 SubMesh 描述前先设置有效总 Bounds，避免 Unity 转换无效 MinMaxAABB。
                _mesh.bounds = meshBounds;
                var subMeshes = new SubMeshDescriptor[_sortedMaterialGroups.Count];
                indexOffset = 0;
                for (int groupIndex = 0; groupIndex < _sortedMaterialGroups.Count; groupIndex++)
                {
                    int groupIndexCount = _sortedMaterialGroups[groupIndex].Indices.Count;
                    subMeshes[groupIndex] =
                        new SubMeshDescriptor(indexOffset, groupIndexCount, MeshTopology.Triangles)
                        {
                            baseVertex = 0,
                            firstVertex = 0,
                            vertexCount = _visibleVertexCount,
                            bounds = meshBounds,
                        };
                    indexOffset += groupIndexCount;
                }

                _mesh.SetSubMeshes(
                    subMeshes,
                    0,
                    subMeshes.Length,
                    MeshUpdateFlags.DontRecalculateBounds
                    | MeshUpdateFlags.DontValidateIndices
                    | MeshUpdateFlags.DontNotifyMeshUsers);
                Renderer.sharedMaterials = materials;
                Renderer.enabled = true;
            }

            private void EnsureVertexBufferCapacity(int required)
            {
                if (_vertexBufferCapacity >= required)
                {
                    return;
                }

                _vertexBufferCapacity = DoubledCapacity(
                    _vertexBufferCapacity,
                    InitialVertexCapacity,
                    required);
                _mesh.SetVertexBufferParams(_vertexBufferCapacity, VertexLayout);
            }

            private void EnsureIndexBufferCapacity(int required)
            {
                if (_indexBufferCapacity >= required)
                {
                    return;
                }

                _indexBufferCapacity = DoubledCapacity(
                    _indexBufferCapacity,
                    InitialIndexCapacity,
                    required);
                _mesh.SetIndexBufferParams(_indexBufferCapacity, IndexFormat.UInt32);
            }

            private void CompleteJob()
            {
                if (!_jobScheduled)
                {
                    return;
                }

                _jobHandle.Complete();
                _jobScheduled = false;
            }

            private static int DoubledCapacity(int current, int initial, int required)
            {
                int capacity = current > 0 ? current : initial;
                while (capacity < required)
                {
                    if (capacity > int.MaxValue / 2)
                    {
                        return required;
                    }

                    capacity *= 2;
                }

                return capacity;
            }
        }

        private sealed class LabelSlot
        {
            public int Generation;
            public int ActiveIndex = -1;
            public LabelRecord Record;
        }

        private sealed class LabelRecord
        {
            public LabelRecord(
                LabelGeometry geometry,
                SettlementTextAnimationKind kind,
                Vector3 anchor,
                float startTime,
                float duration,
                float visualScale,
                float rise,
                bool holdUntilCleared,
                bool autoRelease,
                int sortingOrder)
            {
                Geometry = geometry;
                Kind = kind;
                Anchor = anchor;
                StartTime = startTime;
                Duration = duration;
                VisualScale = visualScale;
                Rise = rise;
                HoldUntilCleared = holdUntilCleared;
                AutoRelease = autoRelease;
                SortingOrder = sortingOrder;
            }

            public LabelGeometry Geometry { get; }
            public SettlementTextAnimationKind Kind { get; }
            public Vector3 Anchor { get; }
            public float StartTime { get; }
            public float Duration { get; }
            public float VisualScale { get; }
            public float Rise { get; }
            public bool HoldUntilCleared { get; }
            public bool AutoRelease { get; }
            public int SortingOrder { get; }
        }

        private sealed class LabelGeometry
        {
            public readonly List<GeometryPart> Parts = new();
            public int VertexCount { get; private set; }

            public void AddPart(GeometryPart part)
            {
                if (part == null || part.Vertices.Length == 0)
                {
                    return;
                }

                Parts.Add(part);
                VertexCount += part.Vertices.Length;
            }
        }

        private sealed class GeometryPart
        {
            public GeometryPart(
                Material material,
                SourceVertex[] vertices,
                int[] triangles,
                int renderLayer)
            {
                Material = material;
                Vertices = vertices ?? Array.Empty<SourceVertex>();
                Triangles = triangles ?? Array.Empty<int>();
                RenderLayer = renderLayer;
            }

            public Material Material { get; }
            public SourceVertex[] Vertices { get; }
            public int[] Triangles { get; }
            public int RenderLayer { get; }
        }

        private readonly struct SourceVertex
        {
            public SourceVertex(
                Vector3 position,
                Vector3 normal,
                Vector4 tangent,
                Vector4 uv0,
                Vector2 uv2,
                Color32 color)
            {
                Position = position;
                Normal = normal;
                Tangent = tangent;
                Uv0 = uv0;
                Uv2 = uv2;
                Color = color;
            }

            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector4 Tangent;
            public readonly Vector4 Uv0;
            public readonly Vector2 Uv2;
            public readonly Color32 Color;
        }

        private sealed class MaterialGroup
        {
            public MaterialGroup(Material material, int renderLayer, int creationOrder)
            {
                Material = material;
                RenderLayer = renderLayer;
                CreationOrder = creationOrder;
            }

            public Material Material { get; }
            public int RenderLayer { get; }
            public int CreationOrder { get; }
            public List<int> Indices { get; } = new();

            public static int Compare(MaterialGroup left, MaterialGroup right)
            {
                int layer = left.RenderLayer.CompareTo(right.RenderLayer);
                return layer != 0 ? layer : left.CreationOrder.CompareTo(right.CreationOrder);
            }
        }
    }
}
