using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct DishShadowSnapshot
    {
        public DishShadowSnapshot(
            Sprite haloSprite,
            Matrix4x4 haloMatrix,
            Color32 haloColor,
            bool haloFlipX,
            bool haloFlipY,
            Sprite coreSprite,
            Matrix4x4 coreMatrix,
            Color32 coreColor,
            bool coreFlipX,
            bool coreFlipY)
        {
            HaloSprite = haloSprite;
            HaloMatrix = haloMatrix;
            HaloColor = haloColor;
            HaloFlipX = haloFlipX;
            HaloFlipY = haloFlipY;
            CoreSprite = coreSprite;
            CoreMatrix = coreMatrix;
            CoreColor = coreColor;
            CoreFlipX = coreFlipX;
            CoreFlipY = coreFlipY;
        }

        public Sprite HaloSprite { get; }
        public Matrix4x4 HaloMatrix { get; }
        public Color32 HaloColor { get; }
        public bool HaloFlipX { get; }
        public bool HaloFlipY { get; }
        public Sprite CoreSprite { get; }
        public Matrix4x4 CoreMatrix { get; }
        public Color32 CoreColor { get; }
        public bool CoreFlipX { get; }
        public bool CoreFlipY { get; }
    }

    /// <summary>
    /// 把所有稳态食物的 core/halo Tight Sprite 几何合进一个无纹理动态 Mesh。
    /// 拓扑变化和顶点/颜色流变化分开处理，同一帧只上传一次。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    internal sealed class DishShadowBatchRenderer : MonoBehaviour
    {
        private const string ShaderName = "GourmetProject/DishShadowBatch";

        private sealed class Slot
        {
            public DishPieceView Source;
            public Sprite HaloSprite;
            public Vector2[] HaloVertices;
            public ushort[] HaloTriangles;
            public int HaloVertexStart;
            public Sprite CoreSprite;
            public Vector2[] CoreVertices;
            public ushort[] CoreTriangles;
            public int CoreVertexStart;
            public DishShadowSnapshot LastSnapshot;
            public bool HasSnapshot;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<DishPieceView, Slot> _slotBySource = new Dictionary<DishPieceView, Slot>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<int> _triangles = new List<int>();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private bool _topologyDirty;
        private bool _streamDirty;
        private int _topologyRevision;
        private int _streamUploadCount;

        internal int RegisteredCount => _slots.Count;
        internal int VertexCount => _mesh != null ? _mesh.vertexCount : 0;
        internal int IndexCount => _mesh != null && _mesh.subMeshCount > 0 ? (int)_mesh.GetIndexCount(0) : 0;
        internal int TopologyRevision => _topologyRevision;
        internal int StreamUploadCount => _streamUploadCount;
        internal Mesh Mesh => _mesh;
        internal MeshRenderer Renderer => _meshRenderer;

        internal bool Register(DishPieceView source)
        {
            if (source == null || !source.TryCaptureShadowSnapshot(transform, out _))
            {
                return false;
            }

            EnsureRuntimeObjects();
            if (_slotBySource.ContainsKey(source))
            {
                source.NotifyShadowBatchRegistered(this);
                return true;
            }

            var slot = new Slot { Source = source };
            _slots.Add(slot);
            _slotBySource.Add(source, slot);
            _topologyDirty = true;
            source.NotifyShadowBatchRegistered(this);
            return true;
        }

        internal void Unregister(DishPieceView source)
        {
            if (source == null || !_slotBySource.Remove(source, out Slot slot))
            {
                return;
            }

            _slots.Remove(slot);
            source.NotifyShadowBatchReleased(this);
            _topologyDirty = true;
        }

        internal void MarkTopologyDirty(DishPieceView source = null)
        {
            if (source == null || _slotBySource.ContainsKey(source))
            {
                _topologyDirty = true;
            }
        }

        internal void MarkStreamDirty(DishPieceView source = null)
        {
            if (source == null || _slotBySource.ContainsKey(source))
            {
                _streamDirty = true;
            }
        }

        internal void FlushPendingChanges()
        {
            RemoveDestroyedSources();
            DetectSourceChanges();
            if (_topologyDirty)
            {
                RebuildTopology();
                return;
            }

            if (_streamDirty)
            {
                UploadStream();
            }
        }

        private void LateUpdate()
        {
            FlushPendingChanges();
        }

        private void DetectSourceChanges()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (!slot.Source.TryCaptureShadowSnapshot(transform, out DishShadowSnapshot snapshot))
                {
                    _topologyDirty = true;
                    continue;
                }

                if (slot.HaloSprite != snapshot.HaloSprite || slot.CoreSprite != snapshot.CoreSprite)
                {
                    _topologyDirty = true;
                    continue;
                }

                if (!slot.HasSnapshot || !SnapshotStreamEquals(slot.LastSnapshot, snapshot))
                {
                    _streamDirty = true;
                }
            }
        }

        private void RebuildTopology()
        {
            EnsureRuntimeObjects();
            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();

            int totalVertices = 0;
            int totalIndices = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (!slot.Source.TryCaptureShadowSnapshot(transform, out DishShadowSnapshot snapshot))
                {
                    continue;
                }

                slot.HaloSprite = snapshot.HaloSprite;
                slot.HaloVertices = snapshot.HaloSprite.vertices;
                slot.HaloTriangles = snapshot.HaloSprite.triangles;
                slot.CoreSprite = snapshot.CoreSprite;
                slot.CoreVertices = snapshot.CoreSprite.vertices;
                slot.CoreTriangles = snapshot.CoreSprite.triangles;
                totalVertices += slot.HaloVertices.Length + slot.CoreVertices.Length;
                totalIndices += slot.HaloTriangles.Length + slot.CoreTriangles.Length;
            }

            EnsureListCapacity(_vertices, totalVertices);
            EnsureListCapacity(_colors, totalVertices);
            EnsureListCapacity(_triangles, totalIndices);

            // 所有 halo 先写入，再写 core，保持旧版两个全局 sortingOrder 的覆盖关系。
            for (int i = 0; i < _slots.Count; i++)
            {
                AppendGeometry(_slots[i], halo: true);
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                AppendGeometry(_slots[i], halo: false);
            }

            _mesh.Clear();
            _mesh.indexFormat = totalVertices > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            UpdateWorkingStream();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0, true);
            _mesh.RecalculateBounds();
            _meshRenderer.enabled = _triangles.Count > 0;
            _topologyRevision++;
            _streamUploadCount++;
            _topologyDirty = false;
            _streamDirty = false;
            ApplyBatchOwnership();
        }

        private void AppendGeometry(Slot slot, bool halo)
        {
            Vector2[] sourceVertices = halo ? slot.HaloVertices : slot.CoreVertices;
            ushort[] sourceTriangles = halo ? slot.HaloTriangles : slot.CoreTriangles;
            if (sourceVertices == null || sourceTriangles == null)
            {
                return;
            }

            int vertexStart = _vertices.Count;
            if (halo)
            {
                slot.HaloVertexStart = vertexStart;
            }
            else
            {
                slot.CoreVertexStart = vertexStart;
            }

            for (int i = 0; i < sourceVertices.Length; i++)
            {
                _vertices.Add(Vector3.zero);
                _colors.Add(default);
            }

            for (int i = 0; i < sourceTriangles.Length; i++)
            {
                _triangles.Add(vertexStart + sourceTriangles[i]);
            }
        }

        private void UploadStream()
        {
            if (_mesh == null)
            {
                return;
            }

            UpdateWorkingStream();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.RecalculateBounds();
            _streamUploadCount++;
            _streamDirty = false;
            ApplyBatchOwnership();
        }

        private void UpdateWorkingStream()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (!slot.Source.TryCaptureShadowSnapshot(transform, out DishShadowSnapshot snapshot))
                {
                    continue;
                }

                WriteStream(
                    slot.HaloVertices,
                    slot.HaloVertexStart,
                    snapshot.HaloMatrix,
                    snapshot.HaloColor,
                    snapshot.HaloFlipX,
                    snapshot.HaloFlipY);
                WriteStream(
                    slot.CoreVertices,
                    slot.CoreVertexStart,
                    snapshot.CoreMatrix,
                    snapshot.CoreColor,
                    snapshot.CoreFlipX,
                    snapshot.CoreFlipY);
                slot.LastSnapshot = snapshot;
                slot.HasSnapshot = true;
            }
        }

        private void WriteStream(
            Vector2[] sourceVertices,
            int vertexStart,
            Matrix4x4 matrix,
            Color32 color,
            bool flipX,
            bool flipY)
        {
            if (sourceVertices == null)
            {
                return;
            }

            float xSign = flipX ? -1f : 1f;
            float ySign = flipY ? -1f : 1f;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                Vector2 source = sourceVertices[i];
                _vertices[vertexStart + i] = matrix.MultiplyPoint3x4(
                    new Vector3(source.x * xSign, source.y * ySign, 0f));
                _colors[vertexStart + i] = color;
            }
        }

        private void RemoveDestroyedSources()
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                Slot slot = _slots[i];
                if (slot.Source != null)
                {
                    continue;
                }

                _slots.RemoveAt(i);
                _topologyDirty = true;
            }

            if (_topologyDirty && _slotBySource.Count != _slots.Count)
            {
                _slotBySource.Clear();
                for (int i = 0; i < _slots.Count; i++)
                {
                    _slotBySource[_slots[i].Source] = _slots[i];
                }
            }
        }

        private void ApplyBatchOwnership()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].Source.NotifyShadowBatchVisible(this);
            }
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
                _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;
                _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                BattleSorting.Apply(_meshRenderer, BattleSorting.DishShadow, BattleSorting.OrderShadow);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "RuntimeDishShadowBatch",
                    hideFlags = HideFlags.DontSave,
                };
                _mesh.MarkDynamic();
                _meshFilter.sharedMesh = _mesh;
            }

            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/DishShadowBatch") ?? Shader.Find(ShaderName);
                if (shader == null)
                {
                    throw new InvalidOperationException($"缺少食物阴影批渲染 Shader：{ShaderName}");
                }

                _material = new Material(shader)
                {
                    name = "RuntimeDishShadowBatch",
                    hideFlags = HideFlags.DontSave,
                };
                _meshRenderer.sharedMaterial = _material;
            }
        }

        internal void ReleaseRuntimeResources(bool destroyImmediately = false)
        {
            ReleaseRegistrations();
            Mesh ownedMesh = _mesh;
            Material ownedMaterial = _material;
            _mesh = null;
            _material = null;
            if (_meshFilter != null)
            {
                _meshFilter.sharedMesh = null;
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.sharedMaterial = null;
                _meshRenderer.enabled = false;
            }

            DestroyOwnedObject(ownedMesh, destroyImmediately);
            DestroyOwnedObject(ownedMaterial, destroyImmediately);
        }

        private void ReleaseRegistrations()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Source != null)
                {
                    _slots[i].Source.NotifyShadowBatchReleased(this);
                }
            }

            _slots.Clear();
            _slotBySource.Clear();
        }

        private void OnDestroy()
        {
            ReleaseRuntimeResources();
        }

        private static bool SnapshotStreamEquals(DishShadowSnapshot left, DishShadowSnapshot right)
        {
            return MatrixApproximately(left.HaloMatrix, right.HaloMatrix)
                && left.HaloColor.Equals(right.HaloColor)
                && left.HaloFlipX == right.HaloFlipX
                && left.HaloFlipY == right.HaloFlipY
                && MatrixApproximately(left.CoreMatrix, right.CoreMatrix)
                && left.CoreColor.Equals(right.CoreColor)
                && left.CoreFlipX == right.CoreFlipX
                && left.CoreFlipY == right.CoreFlipY;
        }

        private static bool MatrixApproximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!Mathf.Approximately(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void DestroyOwnedObject(UnityEngine.Object owned, bool destroyImmediately)
        {
            if (owned == null)
            {
                return;
            }

            if (Application.isPlaying && !destroyImmediately)
            {
                Destroy(owned);
            }
            else
            {
                DestroyImmediate(owned);
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
