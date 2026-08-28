using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.Rendering;

namespace GourmetProject.Game.Presentation.Battle
{
    internal readonly struct PulseUpdate
    {
        public PulseUpdate(GridPos position, float amount)
        {
            Position = position;
            Amount = amount;
        }

        public GridPos Position { get; }
        public float Amount { get; }
    }

    /// <summary>
    /// 把稳定餐桌格合并到一个动态 Mesh。拓扑只在格集合或布局变化时重建；
    /// 颜色、禁用态与结算脉冲通过顶点流更新，并在一帧内合并成一次上传。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    internal sealed class DiningTableBatchRenderer : MonoBehaviour
    {
        private const float PulseScale = 0.06f;
        private const string ShaderName = "GourmetProject/DiningTableBatch";
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");

        private sealed class CellState
        {
            public Color BaseColor = Color.white;
            public Color FeedbackColor = Color.white;
            public bool FeedbackActive;
            public bool Debuffed;
            public float Pulse;
        }

        private readonly HashSet<GridPos> _cellSet = new HashSet<GridPos>();
        private readonly List<GridPos> _orderedCells = new List<GridPos>();
        private readonly Dictionary<GridPos, int> _vertexStarts = new Dictionary<GridPos, int>();
        private readonly Dictionary<GridPos, Vector3> _cellCenters = new Dictionary<GridPos, Vector3>();
        private readonly Dictionary<GridPos, CellState> _states = new Dictionary<GridPos, CellState>();
        private readonly List<Vector3> _baseOffsets = new List<Vector3>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv0 = new List<Vector2>();
        private readonly List<Vector4> _cellData = new List<Vector4>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<int> _triangles = new List<int>();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private Sprite _sprite;
        private Matrix4x4 _visualMatrix;
        private int _sourceVertexCount;
        private int _layoutWidth;
        private int _layoutHeight;
        private float _cellSize;
        private float _gap;
        private bool _buffersDirty;
        private int _geometryRevision;
        private int _streamUploadCount;

        internal int CellCount => _orderedCells.Count;
        internal int VertexCount => _mesh != null ? _mesh.vertexCount : 0;
        internal int IndexCount => _mesh != null ? (int)_mesh.GetIndexCount(0) : 0;
        internal Mesh Mesh => _mesh;
        internal MeshRenderer Renderer => _meshRenderer;
        internal int GeometryRevision => _geometryRevision;
        internal int StreamUploadCount => _streamUploadCount;

        internal bool Contains(GridPos position) => _cellSet.Contains(position);

        internal void SetLayout(
            IReadOnlyCollection<GridPos> cells,
            DiningTableCoordinateMapper mapper,
            Sprite sprite,
            Matrix4x4 visualMatrix)
        {
            EnsureRuntimeObjects();
            if (cells == null || mapper == null || sprite == null)
            {
                Clear();
                return;
            }

            bool topologyChanged = !_cellSet.SetEquals(cells);
            bool geometryChanged = topologyChanged
                || _sprite != sprite
                || _layoutWidth != mapper.Width
                || _layoutHeight != mapper.Height
                || !Mathf.Approximately(_cellSize, mapper.CellSize)
                || !Mathf.Approximately(_gap, mapper.Gap)
                || !MatrixApproximately(_visualMatrix, visualMatrix);
            if (!geometryChanged)
            {
                return;
            }

            _sprite = sprite;
            _visualMatrix = visualMatrix;
            _layoutWidth = mapper.Width;
            _layoutHeight = mapper.Height;
            _cellSize = mapper.CellSize;
            _gap = mapper.Gap;

            _cellSet.Clear();
            _orderedCells.Clear();
            foreach (GridPos position in cells)
            {
                if (_cellSet.Add(position))
                {
                    _orderedCells.Add(position);
                }
            }

            _orderedCells.Sort(CompareGridPositions);
            RemoveStaleStates();
            BuildMesh(mapper);
        }

        internal void SetCellState(GridPos position, Color baseColor, bool debuffed)
        {
            if (!_cellSet.Contains(position))
            {
                return;
            }

            CellState state = EnsureState(position);
            if (state.BaseColor == baseColor && state.Debuffed == debuffed)
            {
                return;
            }

            state.BaseColor = baseColor;
            state.Debuffed = debuffed;
            _buffersDirty = true;
        }

        internal void SetCellFeedback(GridPos position, Color color)
        {
            if (!_cellSet.Contains(position))
            {
                return;
            }

            CellState state = EnsureState(position);
            if (state.FeedbackActive && state.FeedbackColor == color)
            {
                return;
            }

            state.FeedbackColor = color;
            state.FeedbackActive = true;
            _buffersDirty = true;
        }

        internal void ClearCellFeedback(GridPos position)
        {
            if (!_states.TryGetValue(position, out CellState state) || !state.FeedbackActive)
            {
                return;
            }

            state.FeedbackActive = false;
            _buffersDirty = true;
        }

        internal void ClearAllFeedback()
        {
            bool changed = false;
            foreach (CellState state in _states.Values)
            {
                if (!state.FeedbackActive)
                {
                    continue;
                }

                state.FeedbackActive = false;
                changed = true;
            }

            _buffersDirty |= changed;
        }

        internal void SetPulse(GridPos position, float amount)
        {
            if (!_cellSet.Contains(position))
            {
                return;
            }

            CellState state = EnsureState(position);
            float next = Mathf.Clamp01(amount);
            if (Mathf.Approximately(state.Pulse, next))
            {
                return;
            }

            state.Pulse = next;
            _buffersDirty = true;
        }

        internal void SetPulseBatch(IReadOnlyList<PulseUpdate> updates)
        {
            if (updates == null || updates.Count == 0)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < updates.Count; i++)
            {
                PulseUpdate update = updates[i];
                if (!_cellSet.Contains(update.Position))
                {
                    continue;
                }

                CellState state = EnsureState(update.Position);
                float next = Mathf.Clamp01(update.Amount);
                if (Mathf.Approximately(state.Pulse, next))
                {
                    continue;
                }

                state.Pulse = next;
                changed = true;
            }

            _buffersDirty |= changed;
        }

        internal void ClearPulses()
        {
            bool changed = false;
            foreach (CellState state in _states.Values)
            {
                if (Mathf.Approximately(state.Pulse, 0f))
                {
                    continue;
                }

                state.Pulse = 0f;
                changed = true;
            }

            _buffersDirty |= changed;
        }

        internal void FlushPendingChanges()
        {
            if (!_buffersDirty || _mesh == null)
            {
                return;
            }

            UpdateWorkingBuffers();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(1, _cellData);
            _mesh.RecalculateBounds();
            _streamUploadCount++;
            _buffersDirty = false;
        }

        internal void Clear()
        {
            _cellSet.Clear();
            _orderedCells.Clear();
            _vertexStarts.Clear();
            _cellCenters.Clear();
            _states.Clear();
            _baseOffsets.Clear();
            _vertices.Clear();
            _uv0.Clear();
            _cellData.Clear();
            _colors.Clear();
            _triangles.Clear();
            _sourceVertexCount = 0;
            _buffersDirty = false;
            if (_mesh != null)
            {
                _mesh.Clear();
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.enabled = false;
            }
        }

        internal bool TryGetCellState(
            GridPos position,
            out Color baseColor,
            out bool debuffed,
            out bool feedbackActive,
            out float pulse)
        {
            if (_states.TryGetValue(position, out CellState state))
            {
                baseColor = state.BaseColor;
                debuffed = state.Debuffed;
                feedbackActive = state.FeedbackActive;
                pulse = state.Pulse;
                return true;
            }

            baseColor = default;
            debuffed = false;
            feedbackActive = false;
            pulse = 0f;
            return false;
        }

        private void LateUpdate()
        {
            FlushPendingChanges();
        }

        private void BuildMesh(DiningTableCoordinateMapper mapper)
        {
            Vector2[] sourceVertices = _sprite.vertices;
            Vector2[] sourceUv = _sprite.uv;
            ushort[] sourceTriangles = _sprite.triangles;
            _sourceVertexCount = sourceVertices.Length;
            if (_sourceVertexCount == 0 || sourceUv.Length != _sourceVertexCount)
            {
                Clear();
                return;
            }

            int totalVertices = _sourceVertexCount * _orderedCells.Count;
            _baseOffsets.Clear();
            _vertices.Clear();
            _uv0.Clear();
            _cellData.Clear();
            _colors.Clear();
            _triangles.Clear();
            _vertexStarts.Clear();
            _cellCenters.Clear();
            EnsureListCapacity(_baseOffsets, totalVertices);
            EnsureListCapacity(_vertices, totalVertices);
            EnsureListCapacity(_uv0, totalVertices);
            EnsureListCapacity(_cellData, totalVertices);
            EnsureListCapacity(_colors, totalVertices);
            EnsureListCapacity(_triangles, sourceTriangles.Length * _orderedCells.Count);

            Bounds spriteBounds = _sprite.bounds;
            float safeWidth = Mathf.Max(0.0001f, spriteBounds.size.x);
            float safeHeight = Mathf.Max(0.0001f, spriteBounds.size.y);
            for (int cellIndex = 0; cellIndex < _orderedCells.Count; cellIndex++)
            {
                GridPos position = _orderedCells[cellIndex];
                Vector3 center = mapper.CellCenterLocal(position);
                int vertexStart = _vertices.Count;
                _vertexStarts[position] = vertexStart;
                _cellCenters[position] = center;
                EnsureState(position);

                for (int vertexIndex = 0; vertexIndex < _sourceVertexCount; vertexIndex++)
                {
                    Vector2 source = sourceVertices[vertexIndex];
                    Vector3 visualOffset = _visualMatrix.MultiplyPoint3x4(
                        new Vector3(source.x, source.y, 0f)) * mapper.CellSize;
                    _baseOffsets.Add(visualOffset);
                    _vertices.Add(center + visualOffset);
                    _uv0.Add(sourceUv[vertexIndex]);
                    _cellData.Add(new Vector4(
                        (source.x - spriteBounds.min.x) / safeWidth,
                        (source.y - spriteBounds.min.y) / safeHeight,
                        0f,
                        0f));
                    _colors.Add(new Color32(255, 255, 255, 255));
                }

                for (int triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex++)
                {
                    _triangles.Add(vertexStart + sourceTriangles[triangleIndex]);
                }
            }

            _mesh.Clear();
            _mesh.indexFormat = totalVertices > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            UpdateWorkingBuffers();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv0);
            _mesh.SetUVs(1, _cellData);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0, true);
            _mesh.RecalculateBounds();
            _geometryRevision++;
            _streamUploadCount++;
            _meshRenderer.enabled = _orderedCells.Count > 0;
            ApplySpriteTexture(_sprite);
            _buffersDirty = false;
        }

        private void UpdateWorkingBuffers()
        {
            for (int cellIndex = 0; cellIndex < _orderedCells.Count; cellIndex++)
            {
                GridPos position = _orderedCells[cellIndex];
                CellState state = EnsureState(position);
                int vertexStart = _vertexStarts[position];
                Vector3 center = _cellCenters[position];
                float pulse = Mathf.Clamp01(state.Pulse);
                float scale = 1f + PulseScale * pulse;
                Color color = state.FeedbackActive
                    ? new Color(
                        state.FeedbackColor.r,
                        state.FeedbackColor.g,
                        state.FeedbackColor.b,
                        state.BaseColor.a * state.FeedbackColor.a)
                    : state.BaseColor;
                Color pulseTint = new Color(1f, 0.72f, 0.28f, color.a);
                Color32 finalColor = Color.Lerp(color, pulseTint, pulse);

                for (int vertexOffset = 0; vertexOffset < _sourceVertexCount; vertexOffset++)
                {
                    int index = vertexStart + vertexOffset;
                    _vertices[index] = center + _baseOffsets[index] * scale;
                    _colors[index] = finalColor;
                    Vector4 data = _cellData[index];
                    data.z = state.Debuffed ? 1f : 0f;
                    data.w = pulse;
                    _cellData[index] = data;
                }
            }
        }

        private CellState EnsureState(GridPos position)
        {
            if (!_states.TryGetValue(position, out CellState state))
            {
                state = new CellState();
                _states[position] = state;
            }

            return state;
        }

        private void RemoveStaleStates()
        {
            if (_states.Count == 0)
            {
                return;
            }

            var stale = new List<GridPos>();
            foreach (GridPos position in _states.Keys)
            {
                if (!_cellSet.Contains(position))
                {
                    stale.Add(position);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                _states.Remove(stale[i]);
            }
        }

        private void EnsureRuntimeObjects()
        {
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
                if (_meshFilter == null)
                {
                    _meshFilter = gameObject.AddComponent<MeshFilter>();
                }
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
                if (_meshRenderer == null)
                {
                    _meshRenderer = gameObject.AddComponent<MeshRenderer>();
                }

                _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _meshRenderer.receiveShadows = false;
                _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                BattleSorting.Apply(_meshRenderer, BattleSorting.DiningTable, 1);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "RuntimeDiningTableBatch",
                    hideFlags = HideFlags.DontSave,
                };
                _mesh.MarkDynamic();
                _meshFilter.sharedMesh = _mesh;
            }

            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/DiningTableBatch")
                    ?? Shader.Find(ShaderName);
                if (shader == null)
                {
                    throw new InvalidOperationException($"缺少餐桌批渲染 Shader：{ShaderName}");
                }

                _material = new Material(shader)
                {
                    name = "RuntimeDiningTableBatch",
                    hideFlags = HideFlags.DontSave,
                };
                _meshRenderer.sharedMaterial = _material;
            }
        }

        private void ApplySpriteTexture(Sprite sprite)
        {
            if (_material == null || sprite == null)
            {
                return;
            }

            _material.SetTexture(MainTexId, sprite.texture);
            Vector2 uvMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 uvMax = new Vector2(float.MinValue, float.MinValue);
            Vector2[] uvs = sprite.uv;
            for (int i = 0; i < uvs.Length; i++)
            {
                uvMin = Vector2.Min(uvMin, uvs[i]);
                uvMax = Vector2.Max(uvMax, uvs[i]);
            }

            _material.SetVector(
                SpriteUvRectId,
                new Vector4(uvMin.x, uvMin.y, uvMax.x, uvMax.y));
        }

        internal void ReleaseRuntimeResources(bool destroyImmediately = false)
        {
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
            }

            if (ownedMesh != null)
            {
                if (Application.isPlaying && !destroyImmediately)
                {
                    Destroy(ownedMesh);
                }
                else
                {
                    DestroyImmediate(ownedMesh);
                }
            }

            if (ownedMaterial != null)
            {
                if (Application.isPlaying && !destroyImmediately)
                {
                    Destroy(ownedMaterial);
                }
                else
                {
                    DestroyImmediate(ownedMaterial);
                }
            }
        }

        private void OnDestroy()
        {
            ReleaseRuntimeResources();
        }

        private static int CompareGridPositions(GridPos left, GridPos right)
        {
            int row = left.Y.CompareTo(right.Y);
            return row != 0 ? row : left.X.CompareTo(right.X);
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

        private static void EnsureListCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }
    }
}
