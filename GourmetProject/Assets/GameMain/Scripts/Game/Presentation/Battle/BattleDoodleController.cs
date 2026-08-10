using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    public enum BattleDoodleTool
    {
        None,
        Draw,
        Erase
    }

    /// <summary>
    /// 经营挑战涂鸦画布。参考 STS2：笔迹先混合进半分辨率离屏纹理，再由屏幕空间 UI 一次合成到画面上。
    /// 绘制使用普通 Alpha 合成，擦除直接削减画布 Alpha，因此不会生成或长期保留逐笔 Renderer。
    /// </summary>
    public sealed class BattleDoodleController : MonoBehaviour
    {
        private static readonly Color BrushColor = new Color(0.15f, 0.1f, 0.08f, 1f);

        // STS2 的半分辨率 Line2D 宽度分别为 4 / 12；shader 使用半径，因此对应 2 / 6 像素。
        private const float DrawRadius = 2f;
        private const float EraseRadius = 6f;
        private const int ResolutionDivisor = 2;
        private const int MinimumCanvasDimension = 64;
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
        private Vector2 _lastCanvasPoint;
        private BattleDoodleTool _tool = BattleDoodleTool.None;
        private bool _drawing;
        private bool _visible = true;

        public bool IsVisible => _visible;
        public BattleDoodleTool Tool => _tool;
        public RenderTexture CanvasTexture => _currentCanvas;

        private void Update()
        {
            EnsureCanvas();

            if (!_visible || _tool == BattleDoodleTool.None)
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
                Destroy(_strokeMaterial);
                _strokeMaterial = null;
            }
        }

        public void BindOutput(RawImage output)
        {
            if (_output == output)
            {
                SyncOutput();
                return;
            }

            if (_output != null)
            {
                _output.texture = null;
            }

            _output = output;
            if (_output != null)
            {
                _output.raycastTarget = false;
                _output.uvRect = new Rect(0f, 0f, 1f, 1f);
            }

            EnsureCanvas();
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
            _strokeMaterial.SetVector(CanvasSizeId, new Vector4(_currentCanvas.width, _currentCanvas.height, 0f, 0f));
            _strokeMaterial.SetColor(BrushColorId, BrushColor);
            _strokeMaterial.SetFloat(BrushRadiusId, _tool == BattleDoodleTool.Erase ? EraseRadius : DrawRadius);
            _strokeMaterial.SetFloat(EraseId, _tool == BattleDoodleTool.Erase ? 1f : 0f);

            Graphics.Blit(_currentCanvas, destination, _strokeMaterial);
            _currentCanvas = destination;
            SyncOutput();
        }

        private void EnsureCanvas()
        {
            Vector2 pixelSize = OutputPixelSize();
            int width = Mathf.Max(MinimumCanvasDimension, Mathf.RoundToInt(pixelSize.x / ResolutionDivisor));
            int height = Mathf.Max(MinimumCanvasDimension, Mathf.RoundToInt(pixelSize.y / ResolutionDivisor));
            if (_canvasA != null && _canvasB != null && _canvasA.width == width && _canvasA.height == height)
            {
                EnsureStrokeMaterial();
                return;
            }

            RenderTexture oldCanvas = _currentCanvas;
            RenderTexture oldA = _canvasA;
            RenderTexture oldB = _canvasB;

            _canvasA = CreateCanvas(width, height, "BattleDoodleCanvasA");
            _canvasB = CreateCanvas(width, height, "BattleDoodleCanvasB");
            ClearRenderTexture(_canvasA);
            ClearRenderTexture(_canvasB);

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
            if (_output == null)
            {
                return false;
            }

            RectTransform rectTransform = _output.rectTransform;
            Canvas canvas = _output.canvas;
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    screenPoint,
                    eventCamera,
                    out Vector2 localPoint) ||
                !rectTransform.rect.Contains(localPoint))
            {
                return false;
            }

            uv = LocalPointToUv(rectTransform.rect, localPoint);
            return true;
        }

        internal static Vector2 LocalPointToUv(Rect rect, Vector2 localPoint)
        {
            return new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y));
        }

        private Vector2 OutputPixelSize()
        {
            if (_output == null)
            {
                return new Vector2(Screen.width, Screen.height);
            }

            var corners = new Vector3[4];
            _output.rectTransform.GetWorldCorners(corners);
            Canvas canvas = _output.canvas;
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[2]);
            float width = Mathf.Abs(topRight.x - bottomLeft.x);
            float height = Mathf.Abs(topRight.y - bottomLeft.y);
            return width >= 1f && height >= 1f
                ? new Vector2(width, height)
                : new Vector2(Screen.width, Screen.height);
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
                Debug.LogError($"{nameof(BattleDoodleController)} 找不到涂鸦混合 Shader：Resources/{CanvasShaderResourcePath}", this);
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
            _output.enabled = _visible;
        }

        private static RenderTexture CreateCanvas(int width, int height, string textureName)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
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
            Destroy(texture);
            texture = null;
        }
    }
}
