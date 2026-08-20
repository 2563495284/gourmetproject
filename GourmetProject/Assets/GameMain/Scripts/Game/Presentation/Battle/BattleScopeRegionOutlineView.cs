using System.Collections.Generic;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>把一组真实存在的 scope 格子合并成一个不规则外轮廓。</summary>
    public sealed class BattleScopeRegionOutlineView : MonoBehaviour
    {
        internal const int PixelsPerCell = 128;
        internal const int MaskPadding = 20;

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
        private static readonly int GridGlowPixelsId = Shader.PropertyToID("_GridGlowPixels");
        private static readonly int GridGlowAlphaId = Shader.PropertyToID("_GridGlowAlpha");
        private static readonly int GridFlowSpeedId = Shader.PropertyToID("_GridFlowSpeed");
        private static readonly int GridFlowWidthId = Shader.PropertyToID("_GridFlowWidth");
        private static readonly int GridFlowIntensityId = Shader.PropertyToID("_GridFlowIntensity");
        private static readonly int GridStripeDensityId = Shader.PropertyToID("_GridStripeDensity");
        private static readonly int GridStripeSpeedId = Shader.PropertyToID("_GridStripeSpeed");
        private static readonly int GridRevealStartId = Shader.PropertyToID("_GridRevealStart");
        private static readonly int GridRevealDurationId = Shader.PropertyToID("_GridRevealDuration");
        private static readonly int GridVisibilityId = Shader.PropertyToID("_GridVisibility");
        private static readonly int UvInflateId = Shader.PropertyToID("_UvInflate");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");

        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField, Range(0f, 64f)] private float _pulseFrequency = 18f;
        [SerializeField, Range(0f, 64f)] private float _stripeDensity = 9f;
        [SerializeField] private ScopeChannelStyle _persistentStyle = new ScopeChannelStyle
        {
            OutlinePixels = 6f,
            OutlineAlpha = 0.88f,
            FillAlpha = 0.14f,
            GlowPixels = 15f,
            GlowAlpha = 0.32f,
            GlowIntensity = 1.22f,
            PulseSpeed = 0.25f,
            PulseAmplitude = 0.025f,
            FlowSpeed = 0.55f,
            FlowWidth = 0.24f,
            FlowIntensity = 0.52f,
            RevealDuration = 0f,
            FadeOutDuration = 0f,
        };
        [SerializeField] private ScopeChannelStyle _flashStyle = new ScopeChannelStyle
        {
            OutlinePixels = 6f,
            OutlineAlpha = 0.90f,
            FillAlpha = 0.16f,
            GlowPixels = 15f,
            GlowAlpha = 0.32f,
            GlowIntensity = 1.22f,
            PulseSpeed = 1.6f,
            PulseAmplitude = 0.08f,
            FlowSpeed = 2.4f,
            FlowWidth = 0.30f,
            FlowIntensity = 0.70f,
            RevealDuration = 0.10f,
            FadeOutDuration = 0.10f,
        };
        [SerializeField] private ScopeChannelStyle _settlementStyle = new ScopeChannelStyle
        {
            OutlinePixels = 7f,
            OutlineAlpha = 0.95f,
            FillAlpha = 0.18f,
            GlowPixels = 17f,
            GlowAlpha = 0.40f,
            GlowIntensity = 1.36f,
            PulseSpeed = 0.7f,
            PulseAmplitude = 0.06f,
            FlowSpeed = 1.15f,
            FlowWidth = 0.28f,
            FlowIntensity = 0.82f,
            RevealDuration = 0.16f,
            FadeOutDuration = 0.14f,
        };
        private MaterialPropertyBlock _propertyBlock;
        private Texture2D _runtimeMaskTexture;
        private Sprite _runtimeMaskSprite;
        private float _visibility;
        private float _fadeStartVisibility;
        private float _fadeTargetVisibility;
        private float _fadeElapsed;
        private float _fadeDuration;
        private float _activeFadeOutDuration = 0.12f;
        private bool _visibilityAnimating;

        internal BattleScopeHighlightChannel ActiveChannel { get; private set; }

        internal float ActiveVisibility => _visibility;

        internal bool IsVisibilityAnimating => _visibilityAnimating;

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

            ScopeVisualProfile profile = ResolveProfile(channel);
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
            color.a = profile.OutlineAlpha;
            _propertyBlock.SetColor(OutlineColorId, color);
            _propertyBlock.SetFloat(OutlineWidthId, safeWidth);
            _propertyBlock.SetFloat(FillAlphaId, profile.FillAlpha);
            _propertyBlock.SetFloat(GlowIntensityId, profile.GlowIntensity);
            _propertyBlock.SetFloat(PulseSpeedId, profile.PulseSpeed);
            _propertyBlock.SetFloat(PulseAmplitudeId, profile.PulseAmplitude);
            _propertyBlock.SetFloat(PulseFrequencyId, _pulseFrequency);
            _propertyBlock.SetFloat(UseRectMaskId, 0f);
            _propertyBlock.SetFloat(UseGridMaskId, 1f);
            _propertyBlock.SetFloat(GridOutlinePixelsId, profile.OutlinePixels);
            _propertyBlock.SetFloat(GridGlowPixelsId, profile.GlowPixels);
            _propertyBlock.SetFloat(GridGlowAlphaId, profile.GlowAlpha);
            _propertyBlock.SetFloat(GridFlowSpeedId, profile.FlowSpeed);
            _propertyBlock.SetFloat(GridFlowWidthId, profile.FlowWidth);
            _propertyBlock.SetFloat(GridFlowIntensityId, profile.FlowIntensity);
            _propertyBlock.SetFloat(GridStripeDensityId, _stripeDensity);
            _propertyBlock.SetFloat(GridStripeSpeedId, profile.FlowSpeed * 1.10f);
            _propertyBlock.SetFloat(GridRevealStartId, Time.time);
            _propertyBlock.SetFloat(GridRevealDurationId, profile.RevealDuration);
            _propertyBlock.SetFloat(UvInflateId, 1f);
            _propertyBlock.SetVector(SpriteUvRectId, new Vector4(0f, 0f, 1f, 1f));
            _renderer.SetPropertyBlock(_propertyBlock);
            ActiveChannel = channel;
            _activeFadeOutDuration = profile.FadeOutDuration;
            gameObject.SetActive(true);
            if (Application.isPlaying && !UsesImmediateVisibility(channel))
            {
                StartFadeIn(profile.RevealDuration);
            }
            else
            {
                SetVisibilityImmediate(1f, deactivateWhenHidden: false);
            }
        }

        internal ScopeVisualProfile ResolveProfile(BattleScopeHighlightChannel channel)
        {
            ScopeChannelStyle style = channel switch
            {
                BattleScopeHighlightChannel.Flash => _flashStyle,
                BattleScopeHighlightChannel.Settlement => _settlementStyle,
                _ => _persistentStyle,
            };

            ScopeVisualProfile profile = style.ToProfile();
            if (UsesImmediateVisibility(channel))
            {
                profile = profile.WithVisibilityTiming(0f, 0f);
            }

            return profile;
        }

        public void Hide()
        {
            if (!Application.isPlaying || UsesImmediateVisibility(ActiveChannel))
            {
                HideImmediate();
                return;
            }

            StartFadeOut();
        }

        internal static bool UsesImmediateVisibility(BattleScopeHighlightChannel channel)
        {
            return channel == BattleScopeHighlightChannel.Persistent;
        }

        internal void StartFadeIn(float duration)
        {
            gameObject.SetActive(true);
            BeginVisibilityTransition(1f, duration, restartFromZero: true);
        }

        internal void StartFadeOut()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            if (_visibility <= 0.001f)
            {
                HideImmediate();
                return;
            }

            BeginVisibilityTransition(0f, _activeFadeOutDuration, restartFromZero: false);
        }

        internal void AdvanceVisibility(float deltaTime)
        {
            if (!_visibilityAnimating)
            {
                return;
            }

            _fadeElapsed += Mathf.Max(0f, deltaTime);
            float progress = _fadeDuration <= 0.0001f
                ? 1f
                : Mathf.Clamp01(_fadeElapsed / _fadeDuration);
            float eased = progress * progress * (3f - 2f * progress);
            ApplyVisibility(Mathf.Lerp(_fadeStartVisibility, _fadeTargetVisibility, eased));
            if (progress < 1f)
            {
                return;
            }

            _visibilityAnimating = false;
            if (_fadeTargetVisibility <= 0.001f)
            {
                gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            AdvanceVisibility(Time.deltaTime);
        }

        private void BeginVisibilityTransition(
            float targetVisibility,
            float duration,
            bool restartFromZero)
        {
            if (restartFromZero)
            {
                ApplyVisibility(0f);
            }

            _fadeStartVisibility = _visibility;
            _fadeTargetVisibility = Mathf.Clamp01(targetVisibility);
            _fadeElapsed = 0f;
            _fadeDuration = Mathf.Max(0f, duration);
            _visibilityAnimating = !Mathf.Approximately(
                _fadeStartVisibility,
                _fadeTargetVisibility);
            if (!_visibilityAnimating || _fadeDuration <= 0.0001f)
            {
                SetVisibilityImmediate(
                    _fadeTargetVisibility,
                    deactivateWhenHidden: _fadeTargetVisibility <= 0.001f);
            }
        }

        private void ApplyVisibility(float visibility)
        {
            _visibility = Mathf.Clamp01(visibility);
            if (_renderer == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(GridVisibilityId, _visibility);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private void SetVisibilityImmediate(float visibility, bool deactivateWhenHidden)
        {
            _visibilityAnimating = false;
            ApplyVisibility(visibility);
            if (deactivateWhenHidden && _visibility <= 0.001f)
            {
                gameObject.SetActive(false);
            }
        }

        private void HideImmediate()
        {
            SetVisibilityImmediate(0f, deactivateWhenHidden: true);
        }

        private void BuildMaskSprite(
            IReadOnlyList<GridPos> cells,
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            ReleaseRuntimeMask();

            Color32[] pixels = BuildMaskPixels(
                cells,
                minX,
                minY,
                maxX,
                maxY,
                out int width,
                out int height);

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

        internal static Color32[] BuildMaskPixels(
            IReadOnlyList<GridPos> cells,
            int minX,
            int minY,
            int maxX,
            int maxY,
            out int width,
            out int height)
        {
            int columns = Mathf.Max(0, maxX - minX + 1);
            int rows = Mathf.Max(0, maxY - minY + 1);
            width = columns * PixelsPerCell + MaskPadding * 2;
            height = rows * PixelsPerCell + MaskPadding * 2;
            var pixels = new Color32[width * height];
            if (cells == null || columns == 0 || rows == 0)
            {
                return pixels;
            }

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

            return pixels;
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

        [System.Serializable]
        private struct ScopeChannelStyle
        {
            [Range(1f, 8f)] public float OutlinePixels;
            [Range(0f, 1f)] public float OutlineAlpha;
            [Range(0f, 0.25f)] public float FillAlpha;
            [Range(1f, 24f)] public float GlowPixels;
            [Range(0f, 1f)] public float GlowAlpha;
            [Range(0.25f, 3f)] public float GlowIntensity;
            [Range(0f, 8f)] public float PulseSpeed;
            [Range(0f, 0.5f)] public float PulseAmplitude;
            [Range(0f, 4f)] public float FlowSpeed;
            [Range(0.02f, 0.5f)] public float FlowWidth;
            [Range(0f, 1f)] public float FlowIntensity;
            [Range(0f, 0.5f)] public float RevealDuration;
            [Range(0f, 0.5f)] public float FadeOutDuration;

            public ScopeVisualProfile ToProfile()
            {
                return new ScopeVisualProfile(
                    OutlinePixels,
                    OutlineAlpha,
                    FillAlpha,
                    GlowPixels,
                    GlowAlpha,
                    GlowIntensity,
                    PulseSpeed,
                    PulseAmplitude,
                    FlowSpeed,
                    FlowWidth,
                    FlowIntensity,
                    RevealDuration,
                    FadeOutDuration);
            }
        }

        internal readonly struct ScopeVisualProfile
        {
            public ScopeVisualProfile(
                float outlinePixels,
                float outlineAlpha,
                float fillAlpha,
                float glowPixels,
                float glowAlpha,
                float glowIntensity,
                float pulseSpeed,
                float pulseAmplitude,
                float flowSpeed,
                float flowWidth,
                float flowIntensity,
                float revealDuration,
                float fadeOutDuration)
            {
                OutlinePixels = outlinePixels;
                OutlineAlpha = outlineAlpha;
                FillAlpha = fillAlpha;
                GlowPixels = glowPixels;
                GlowAlpha = glowAlpha;
                GlowIntensity = glowIntensity;
                PulseSpeed = pulseSpeed;
                PulseAmplitude = pulseAmplitude;
                FlowSpeed = flowSpeed;
                FlowWidth = flowWidth;
                FlowIntensity = flowIntensity;
                RevealDuration = revealDuration;
                FadeOutDuration = fadeOutDuration;
            }

            public float OutlinePixels { get; }
            public float OutlineAlpha { get; }
            public float FillAlpha { get; }
            public float GlowPixels { get; }
            public float GlowAlpha { get; }
            public float GlowIntensity { get; }
            public float PulseSpeed { get; }
            public float PulseAmplitude { get; }
            public float FlowSpeed { get; }
            public float FlowWidth { get; }
            public float FlowIntensity { get; }
            public float RevealDuration { get; }
            public float FadeOutDuration { get; }

            public ScopeVisualProfile WithVisibilityTiming(float revealDuration, float fadeOutDuration)
            {
                return new ScopeVisualProfile(
                    OutlinePixels,
                    OutlineAlpha,
                    FillAlpha,
                    GlowPixels,
                    GlowAlpha,
                    GlowIntensity,
                    PulseSpeed,
                    PulseAmplitude,
                    FlowSpeed,
                    FlowWidth,
                    FlowIntensity,
                    revealDuration,
                    fadeOutDuration);
            }
        }
    }
}
