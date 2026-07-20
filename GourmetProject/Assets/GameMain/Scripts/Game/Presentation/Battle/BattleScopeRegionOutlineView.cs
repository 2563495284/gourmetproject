using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>把一组真实存在的 scope 格子合并成一个不规则外轮廓。</summary>
    public sealed class BattleScopeRegionOutlineView : MonoBehaviour
    {
        private const int PixelsPerCell = 128;
        private const int MaskPadding = 8;

        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int PulseAmplitudeId = Shader.PropertyToID("_PulseAmplitude");
        private static readonly int PulseFrequencyId = Shader.PropertyToID("_PulseFrequency");
        private static readonly int UseRectMaskId = Shader.PropertyToID("_UseRectMask");
        private static readonly int UseGridMaskId = Shader.PropertyToID("_UseGridMask");
        private static readonly int GridOutlinePixelsId = Shader.PropertyToID("_GridOutlinePixels");
        private static readonly int UvInflateId = Shader.PropertyToID("_UvInflate");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField, Range(1f, 8f)] private float _persistentOutlinePixels = 6f;
        [SerializeField, Range(1f, 8f)] private float _flashOutlinePixels = 6f;
        [SerializeField, Range(0f, 1f)] private float _outlineAlpha = 0.95f;
        [SerializeField, Range(0.25f, 3f)] private float _persistentGlowIntensity = 1.1f;
        [SerializeField, Range(0.25f, 3f)] private float _flashGlowIntensity = 1.2f;
        [SerializeField, Range(0f, 8f)] private float _persistentPulseSpeed = 0.55f;
        [SerializeField, Range(0f, 8f)] private float _flashPulseSpeed = 1.8f;
        [SerializeField, Range(0f, 0.5f)] private float _persistentPulseAmplitude = 0.035f;
        [SerializeField, Range(0f, 0.5f)] private float _flashPulseAmplitude = 0.1f;
        [SerializeField, Range(0f, 64f)] private float _pulseFrequency = 18f;
        private MaterialPropertyBlock _propertyBlock;
        private Texture2D _runtimeMaskTexture;
        private Sprite _runtimeMaskSprite;

        public void Show(
            BattleScopeHighlightChannel channel,
            int layer,
            IReadOnlyList<GridPos> cells,
            int minX,
            int minY,
            int maxX,
            int maxY,
            Vector3 localCenter,
            Vector2 localSize,
            Color color,
            float width,
            Material materialOverride)
        {
            if (_renderer == null
                || cells == null
                || cells.Count == 0
                || maxX < minX
                || maxY < minY)
            {
                return;
            }

            BuildMaskSprite(cells, minX, minY, maxX, maxY);
            if (_runtimeMaskSprite == null)
            {
                return;
            }

            float safeWidth = Mathf.Max(0.001f, width);
            int columns = maxX - minX + 1;
            int rows = maxY - minY + 1;
            transform.localPosition = localCenter;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(
                Mathf.Max(0.001f, localSize.x / columns),
                Mathf.Max(0.001f, localSize.y / rows),
                1f);

            _renderer.sprite = _runtimeMaskSprite;
            _renderer.color = Color.white;
            _renderer.sharedMaterial = materialOverride != null ? materialOverride : _outlineMaterial;
            BattleSorting.Apply(
                _renderer,
                BattleSorting.Fx,
                BattleSorting.OrderScopeRegion + Mathf.Clamp(layer, 0, 63));

            _propertyBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
            color.a = _outlineAlpha;
            _propertyBlock.SetColor(OutlineColorId, color);
            _propertyBlock.SetFloat(OutlineWidthId, safeWidth);
            _propertyBlock.SetFloat(FillAlphaId, 0f);
            _propertyBlock.SetFloat(
                GlowIntensityId,
                channel == BattleScopeHighlightChannel.Persistent
                    ? _persistentGlowIntensity
                    : _flashGlowIntensity);
            _propertyBlock.SetFloat(
                PulseSpeedId,
                channel == BattleScopeHighlightChannel.Persistent
                    ? _persistentPulseSpeed
                    : _flashPulseSpeed);
            _propertyBlock.SetFloat(
                PulseAmplitudeId,
                channel == BattleScopeHighlightChannel.Persistent
                    ? _persistentPulseAmplitude
                    : _flashPulseAmplitude);
            _propertyBlock.SetFloat(PulseFrequencyId, _pulseFrequency);
            _propertyBlock.SetFloat(UseRectMaskId, 0f);
            _propertyBlock.SetFloat(UseGridMaskId, 1f);
            _propertyBlock.SetFloat(
                GridOutlinePixelsId,
                channel == BattleScopeHighlightChannel.Persistent
                    ? _persistentOutlinePixels
                    : _flashOutlinePixels);
            _propertyBlock.SetFloat(UvInflateId, 1f);
            _propertyBlock.SetVector(SpriteUvRectId, new Vector4(0f, 0f, 1f, 1f));
            _renderer.SetPropertyBlock(_propertyBlock);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void BuildMaskSprite(
            IReadOnlyList<GridPos> cells,
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            ReleaseRuntimeMask();

            int columns = maxX - minX + 1;
            int rows = maxY - minY + 1;
            int width = columns * PixelsPerCell + MaskPadding * 2;
            int height = rows * PixelsPerCell + MaskPadding * 2;
            var pixels = new Color32[width * height];

            foreach (GridPos cell in cells)
            {
                int column = cell.X - minX;
                int rowFromBottom = maxY - cell.Y;
                if (column < 0 || column >= columns || rowFromBottom < 0 || rowFromBottom >= rows)
                {
                    continue;
                }

                int startX = MaskPadding + column * PixelsPerCell;
                int startY = MaskPadding + rowFromBottom * PixelsPerCell;
                for (int y = 0; y < PixelsPerCell; y++)
                {
                    int rowOffset = (startY + y) * width + startX;
                    for (int x = 0; x < PixelsPerCell; x++)
                    {
                        pixels[rowOffset + x] = new Color32(255, 255, 255, 255);
                    }
                }
            }

            _runtimeMaskTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "ScopeGridMask",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _runtimeMaskTexture.SetPixels32(pixels);
            _runtimeMaskTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _runtimeMaskSprite = Sprite.Create(
                _runtimeMaskTexture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                PixelsPerCell,
                0u,
                SpriteMeshType.FullRect);
            _runtimeMaskSprite.name = "ScopeGridMask";
        }

        private void OnDestroy()
        {
            ReleaseRuntimeMask();
        }

        private void ReleaseRuntimeMask()
        {
            DestroyRuntimeObject(_runtimeMaskSprite);
            DestroyRuntimeObject(_runtimeMaskTexture);
            _runtimeMaskSprite = null;
            _runtimeMaskTexture = null;
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
