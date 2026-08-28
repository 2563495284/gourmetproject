using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Game.Presentation.Battle
{
    public enum BattleDoodleTool
    {
        None,
        Draw,
        Erase
    }

    /// <summary>
    /// 经营挑战涂鸦画布。笔迹保存在完整餐桌逻辑网格对应的离屏纹理中，屏幕上的 RawImage
    /// 只显示当前餐桌可视包围区。餐桌根移动、缩放或扩格时只更新投影，不重采样已有笔迹。
    /// </summary>
    public sealed class BattleDoodleController : MonoBehaviour
    {
        private static readonly Color BrushColor = new(0.15f, 0.1f, 0.08f, 1f);

        // 60px/格附近时分别约等于旧实现的 2px / 12px 半径；改用格子比例后会随餐桌一起缩放。
        private const float DrawRadiusInCells = 1f / 30f;
        private const float EraseRadiusInCells = 0.2f;
        private const float TargetPixelsPerCell = 64f;
        private const int MaximumCanvasDimension = 2048;
        private const string CanvasShaderResourcePath = "Shaders/BattleDoodleCanvas";

        private static readonly int StrokeStartId = Shader.PropertyToID("_StrokeStart");
        private static readonly int StrokeEndId = Shader.PropertyToID("_StrokeEnd");
        private static readonly int CanvasSizeId = Shader.PropertyToID("_CanvasSize");
        private static readonly int BrushColorId = Shader.PropertyToID("_BrushColor");
        private static readonly int BrushRadiusId = Shader.PropertyToID("_BrushRadius");
        private static readonly int EraseId = Shader.PropertyToID("_Erase");

        private RenderTexture _canvasA;
        private RenderTexture _canvasB;
        private RenderTexture _currentCanvas;
        private Material _strokeMaterial;
        private RawImage _output;
        private Camera _worldCamera;
        private DiningTableCoordinateMapper _mapper;
        private TableFragmentBuilder.PlacementBounds _visibleBounds;
        private Vector2 _lastCanvasPoint;
        private Vector2Int _canvasGridSize;
        private float _canvasPixelsPerCell = TargetPixelsPerCell;
        private BattleDoodleTool _tool = BattleDoodleTool.None;
        private bool _drawing;
        private bool _visible = true;
        private bool _presentationActive;
        private bool _hasTable;

        public bool IsVisible => _visible;
        public bool IsPresentationActive => _presentationActive;
        public BattleDoodleTool Tool => _tool;
        public RenderTexture CanvasTexture => _currentCanvas;

        private bool ShouldRender => _visible && _presentationActive && _hasTable;

        private void Update()
        {
            EnsureCanvas();
            SyncOutputGeometry();

            if (!ShouldRender || _tool == BattleDoodleTool.None)
            {
                _drawing = false;
                return;
            }

            if (WorldInput.PrimaryPressedThisFrame &&
                TryScreenPointToCanvasUv(WorldInput.MouseScreen, out Vector2 start))
            {
                _lastCanvasPoint = start;
                _drawing = true;
                StampSegment(_lastCanvasPoint, _lastCanvasPoint);
            }
            else if (_drawing && WorldInput.PrimaryHeld)
            {
                if (!TryScreenPointToCanvasUv(WorldInput.MouseScreen, out Vector2 next))
                {
                    _drawing = false;
                    return;
                }

                Vector2 canvasDelta = Vector2.Scale(
                    next - _lastCanvasPoint,
                    new Vector2(_currentCanvas.width, _currentCanvas.height));
                if (canvasDelta.sqrMagnitude >= 1f)
                {
                    StampSegment(_lastCanvasPoint, next);
                    _lastCanvasPoint = next;
                }
            }

            if (!WorldInput.PrimaryHeld)
            {
                _drawing = false;
            }
        }

        private void OnDisable()
        {
            _drawing = false;
        }

        private void OnDestroy()
        {
            ReleaseCanvas(ref _canvasA);
            ReleaseCanvas(ref _canvasB);
            _currentCanvas = null;

            if (_strokeMaterial != null)
            {
                DestroyRuntimeObject(_strokeMaterial);
                _strokeMaterial = null;
            }
        }

        /// <summary>
        /// 绑定当前餐桌的逻辑映射。完整纹理尺寸只依赖 DiningTable 的稳定画布 Width/Height，
        /// visible bounds 只控制当前投影，所以后续增加存在格不会移动旧笔迹的纹理坐标。
        /// </summary>
        public void ConfigureTable(
            DiningTableCoordinateMapper mapper,
            GpTable table,
            Camera worldCamera,
            IEnumerable<GridPos> additionalVisibleCells = null)
        {
            _mapper = mapper;
            _worldCamera = worldCamera != null ? worldCamera : Camera.main;
            _hasTable = mapper != null && mapper.Root != null && table != null;
            if (!_hasTable)
            {
                _drawing = false;
                SyncOutput();
                return;
            }

            _visibleBounds = DiningTableLayout.VisibleBounds(table, additionalVisibleCells);
            _canvasGridSize = new Vector2Int(table.Width, table.Height);
            EnsureCanvas();
            SyncOutputGeometry();
            SyncOutput();
        }

        public void BindOutput(RawImage output)
        {
            if (_output == output)
            {
                SyncOutputGeometry();
                SyncOutput();
                return;
            }

            if (_output != null)
            {
                _output.texture = null;
                _output.enabled = false;
            }

            _output = output;
            if (_output != null)
            {
                _output.raycastTarget = false;
            }

            EnsureCanvas();
            SyncOutputGeometry();
            SyncOutput();
        }

        public void SetTool(BattleDoodleTool tool)
        {
            _tool = tool;
            _drawing = false;
            if (_tool != BattleDoodleTool.None)
            {
                _visible = true;
                SyncOutput();
            }
        }

        public BattleDoodleTool ToggleTool(BattleDoodleTool tool)
        {
            SetTool(_tool == tool ? BattleDoodleTool.None : tool);
            return _tool;
        }

        public void Clear()
        {
            _drawing = false;
            EnsureCanvas();
            ClearRenderTexture(_canvasA);
            ClearRenderTexture(_canvasB);
            _currentCanvas = _canvasA;
            SyncOutput();
        }

        /// <summary>玩家级显隐；隐藏会退出工具，但不会删除纹理内容。</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            _drawing = false;
            if (!visible)
            {
                _tool = BattleDoodleTool.None;
            }
            SyncOutput();
        }

        /// <summary>页面级显隐；离开 Food 页面时临时收起，不改玩家显隐选择，也不删除内容。</summary>
        public void SetPresentationActive(bool active)
        {
            _presentationActive = active;
            _drawing = false;
            if (!active)
            {
                _tool = BattleDoodleTool.None;
            }

            if (active)
            {
                SyncOutputGeometry();
            }
            SyncOutput();
        }

        private void StampSegment(Vector2 start, Vector2 end)
        {
            EnsureCanvas();
            if (_currentCanvas == null || _strokeMaterial == null)
            {
                return;
            }

            RenderTexture destination = _currentCanvas == _canvasA ? _canvasB : _canvasA;
            _strokeMaterial.SetVector(StrokeStartId, start);
            _strokeMaterial.SetVector(StrokeEndId, end);
            _strokeMaterial.SetVector(
                CanvasSizeId,
                new Vector4(_currentCanvas.width, _currentCanvas.height, 0f, 0f));
            _strokeMaterial.SetColor(BrushColorId, BrushColor);
            _strokeMaterial.SetFloat(
                BrushRadiusId,
                BrushRadiusPixels(_canvasPixelsPerCell, _tool));
            _strokeMaterial.SetFloat(EraseId, _tool == BattleDoodleTool.Erase ? 1f : 0f);

            Graphics.Blit(_currentCanvas, destination, _strokeMaterial);
            _currentCanvas = destination;
            SyncOutput();
        }

        private void EnsureCanvas()
        {
            if (_canvasGridSize.x <= 0 || _canvasGridSize.y <= 0)
            {
                return;
            }

            Vector2Int size = CanvasSizeForGrid(_canvasGridSize.x, _canvasGridSize.y);
            _canvasPixelsPerCell = Mathf.Min(
                size.x / (float)_canvasGridSize.x,
                size.y / (float)_canvasGridSize.y);
            if (_canvasA != null && _canvasB != null &&
                _canvasA.width == size.x && _canvasA.height == size.y)
            {
                EnsureStrokeMaterial();
                return;
            }

            RenderTexture oldCanvas = _currentCanvas;
            RenderTexture oldA = _canvasA;
            RenderTexture oldB = _canvasB;

            _canvasA = CreateCanvas(size.x, size.y, "BattleDoodleCanvasA");
            _canvasB = CreateCanvas(size.x, size.y, "BattleDoodleCanvasB");
            ClearRenderTexture(_canvasA);
            ClearRenderTexture(_canvasB);

            // 同一轮经营的完整逻辑画布尺寸通常不变；分辨率变化时保留已有内容作为安全回退。
            if (oldCanvas != null)
            {
                Graphics.Blit(oldCanvas, _canvasA);
            }

            _currentCanvas = _canvasA;
            ReleaseCanvas(ref oldA);
            ReleaseCanvas(ref oldB);
            EnsureStrokeMaterial();
            SyncOutput();
        }

        private bool TryScreenPointToCanvasUv(Vector2 screenPoint, out Vector2 uv)
        {
            uv = default;
            if (!_hasTable || _mapper?.Root == null || _worldCamera == null)
            {
                return false;
            }

            float depth = _worldCamera.WorldToScreenPoint(_mapper.Root.position).z;
            Vector3 world = _worldCamera.ScreenToWorldPoint(
                new Vector3(screenPoint.x, screenPoint.y, depth));
            Vector3 local = _mapper.Root.InverseTransformPoint(world);
            Rect visibleLocalRect = VisibleLocalRect(_mapper, _visibleBounds);
            if (!visibleLocalRect.Contains(new Vector2(local.x, local.y)))
            {
                return false;
            }

            uv = CanvasUvForLocalPoint(_mapper, local);
            return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
        }

        private void SyncOutputGeometry()
        {
            if (_output == null || !_hasTable || _mapper?.Root == null || _worldCamera == null)
            {
                return;
            }

            RectTransform outputRect = _output.rectTransform;
            if (outputRect.parent is not RectTransform parentRect)
            {
                return;
            }

            Rect screenRect = ProjectVisibleBoundsToScreenRect(
                _mapper,
                _visibleBounds,
                _worldCamera);
            Canvas canvas = _output.canvas;
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect,
                    screenRect.min,
                    eventCamera,
                    out Vector2 localMin) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect,
                    screenRect.max,
                    eventCamera,
                    out Vector2 localMax))
            {
                return;
            }

            Vector2 min = Vector2.Min(localMin, localMax);
            Vector2 max = Vector2.Max(localMin, localMax);
            outputRect.anchorMin = new Vector2(0.5f, 0.5f);
            outputRect.anchorMax = new Vector2(0.5f, 0.5f);
            outputRect.pivot = new Vector2(0.5f, 0.5f);
            outputRect.localRotation = Quaternion.identity;
            outputRect.localScale = Vector3.one;
            outputRect.anchoredPosition = (min + max) * 0.5f - parentRect.rect.center;
            outputRect.sizeDelta = max - min;
            _output.uvRect = CanvasUvRectForBounds(_mapper, _visibleBounds);
        }

        internal static Vector2Int CanvasSizeForGrid(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            float pixelsPerCell = Mathf.Min(
                TargetPixelsPerCell,
                MaximumCanvasDimension / (float)Mathf.Max(width, height));
            return new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(width * pixelsPerCell), 1, MaximumCanvasDimension),
                Mathf.Clamp(Mathf.RoundToInt(height * pixelsPerCell), 1, MaximumCanvasDimension));
        }

        internal static float BrushRadiusPixels(float pixelsPerCell, BattleDoodleTool tool)
        {
            float radiusInCells = tool == BattleDoodleTool.Erase
                ? EraseRadiusInCells
                : DrawRadiusInCells;
            return Mathf.Max(0.5f, pixelsPerCell * radiusInCells);
        }

        internal static Vector2 CanvasUvForLocalPoint(
            DiningTableCoordinateMapper mapper,
            Vector3 localPoint)
        {
            if (mapper == null || mapper.WorldWidth <= 0f || mapper.WorldHeight <= 0f)
            {
                return Vector2.zero;
            }

            return new Vector2(
                Mathf.InverseLerp(-mapper.WorldWidth * 0.5f, mapper.WorldWidth * 0.5f, localPoint.x),
                Mathf.InverseLerp(-mapper.WorldHeight * 0.5f, mapper.WorldHeight * 0.5f, localPoint.y));
        }

        internal static Rect CanvasUvRectForBounds(
            DiningTableCoordinateMapper mapper,
            TableFragmentBuilder.PlacementBounds bounds)
        {
            Rect local = VisibleLocalRect(mapper, bounds);
            Vector2 min = CanvasUvForLocalPoint(mapper, new Vector3(local.xMin, local.yMin, 0f));
            Vector2 max = CanvasUvForLocalPoint(mapper, new Vector3(local.xMax, local.yMax, 0f));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        internal static Rect ProjectVisibleBoundsToScreenRect(
            DiningTableCoordinateMapper mapper,
            TableFragmentBuilder.PlacementBounds bounds,
            Camera camera)
        {
            if (mapper?.Root == null || camera == null)
            {
                return default;
            }

            Rect local = VisibleLocalRect(mapper, bounds);
            Vector3[] corners =
            {
                new(local.xMin, local.yMin, 0f),
                new(local.xMin, local.yMax, 0f),
                new(local.xMax, local.yMax, 0f),
                new(local.xMax, local.yMin, 0f),
            };
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 screen = camera.WorldToScreenPoint(mapper.Root.TransformPoint(corners[i]));
                minX = Mathf.Min(minX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxX = Mathf.Max(maxX, screen.x);
                maxY = Mathf.Max(maxY, screen.y);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static Rect VisibleLocalRect(
            DiningTableCoordinateMapper mapper,
            TableFragmentBuilder.PlacementBounds bounds)
        {
            if (mapper == null)
            {
                return default;
            }

            float halfCell = mapper.CellSize * 0.5f;
            Vector3 topLeft = mapper.CellCenterLocal(new GridPos(bounds.MinX, bounds.MinY));
            Vector3 bottomRight = mapper.CellCenterLocal(new GridPos(bounds.MaxX, bounds.MaxY));
            return Rect.MinMaxRect(
                topLeft.x - halfCell,
                bottomRight.y - halfCell,
                bottomRight.x + halfCell,
                topLeft.y + halfCell);
        }

        private void EnsureStrokeMaterial()
        {
            if (_strokeMaterial != null)
            {
                return;
            }

            Shader shader = Resources.Load<Shader>(CanvasShaderResourcePath);
            if (shader == null)
            {
                Debug.LogError(
                    $"{nameof(BattleDoodleController)} 找不到涂鸦混合 Shader：Resources/{CanvasShaderResourcePath}",
                    this);
                return;
            }

            _strokeMaterial = new Material(shader)
            {
                name = "Battle Doodle Stroke Material",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void SyncOutput()
        {
            if (_output == null)
            {
                return;
            }

            _output.texture = _currentCanvas;
            _output.enabled = ShouldRender;
        }

        private static RenderTexture CreateCanvas(int width, int height, string textureName)
        {
            var texture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        private static void ClearRenderTexture(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        private static void ReleaseCanvas(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            DestroyRuntimeObject(texture);
            texture = null;
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }
    }
}
