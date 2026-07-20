using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>把一组 scope 格子的包围盒画成单个矩形外轮廓。</summary>
    public sealed class BattleScopeRegionOutlineView : MonoBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int PulseAmplitudeId = Shader.PropertyToID("_PulseAmplitude");
        private static readonly int PulseFrequencyId = Shader.PropertyToID("_PulseFrequency");
        private static readonly int UseRectMaskId = Shader.PropertyToID("_UseRectMask");
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField, Range(0.25f, 3f)] private float _persistentGlowIntensity = 1.5f;
        [SerializeField, Range(0.25f, 3f)] private float _flashGlowIntensity = 1.8f;
        [SerializeField, Range(0f, 8f)] private float _persistentPulseSpeed = 0.55f;
        [SerializeField, Range(0f, 8f)] private float _flashPulseSpeed = 1.8f;
        [SerializeField, Range(0f, 0.5f)] private float _persistentPulseAmplitude = 0.035f;
        [SerializeField, Range(0f, 0.5f)] private float _flashPulseAmplitude = 0.1f;
        [SerializeField, Range(0f, 64f)] private float _pulseFrequency = 18f;
        [SerializeField, Min(0f)] private float _outsidePaddingMultiplier = 1f;

        private MaterialPropertyBlock _propertyBlock;

        public void Show(
            BattleScopeHighlightChannel channel,
            int layer,
            Vector3 localCenter,
            Vector2 localSize,
            Color color,
            float width,
            Material materialOverride)
        {
            if (_renderer == null || _renderer.sprite == null)
            {
                return;
            }

            Sprite sprite = _renderer.sprite;
            float safeWidth = Mathf.Max(0.001f, width);
            float padding = safeWidth * Mathf.Max(0f, _outsidePaddingMultiplier);
            Vector2 visualSize = new Vector2(
                Mathf.Max(0.001f, localSize.x + padding * 2f),
                Mathf.Max(0.001f, localSize.y + padding * 2f));
            Vector2 spriteSize = sprite.bounds.size;

            transform.localPosition = localCenter;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(
                spriteSize.x > 0f ? visualSize.x / spriteSize.x : visualSize.x,
                spriteSize.y > 0f ? visualSize.y / spriteSize.y : visualSize.y,
                1f);

            _renderer.sprite = sprite;
            _renderer.color = Color.white;
            _renderer.sharedMaterial = materialOverride != null ? materialOverride : _outlineMaterial;
            BattleSorting.Apply(
                _renderer,
                BattleSorting.Fx,
                BattleSorting.OrderScopeRegion + Mathf.Clamp(layer, 0, 63));

            _propertyBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
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
            _propertyBlock.SetFloat(UseRectMaskId, 1f);
            _propertyBlock.SetVector(RectSizeId, new Vector4(visualSize.x, visualSize.y, 0f, 0f));
            _propertyBlock.SetVector(SpriteUvRectId, SpriteUvRect(sprite));
            _renderer.SetPropertyBlock(_propertyBlock);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private static Vector4 SpriteUvRect(Sprite sprite)
        {
            Vector2[] uvs = sprite != null ? sprite.uv : null;
            if (uvs == null || uvs.Length == 0)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            Vector2 min = uvs[0];
            Vector2 max = uvs[0];
            for (int i = 1; i < uvs.Length; i++)
            {
                min = Vector2.Min(min, uvs[i]);
                max = Vector2.Max(max, uvs[i]);
            }

            return new Vector4(min.x, min.y, max.x, max.y);
        }
    }
}
