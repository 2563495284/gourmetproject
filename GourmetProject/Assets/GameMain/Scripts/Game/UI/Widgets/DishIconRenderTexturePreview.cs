using System;
using System.Collections.Generic;
using BreakInfinity;
using DG.Tweening;
using GourmetProject.Core.Utility;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI.Widgets
{
    public enum DishIconPreviewMode
    {
        Card,
        Warehouse,
    }

    public readonly struct DishPreviewRequest
    {
        public DishPreviewRequest(
            DishDef dish,
            Sprite sprite,
            BigDouble value,
            IReadOnlyList<string> flavorIds,
            DishIconPreviewMode mode,
            int? rotationIndex,
            float visualSeed)
        {
            Dish = dish;
            Sprite = sprite;
            Value = value;
            FlavorIds = flavorIds;
            Mode = mode;
            RotationIndex = rotationIndex;
            VisualSeed = visualSeed;
        }

        public DishDef Dish { get; }

        public Sprite Sprite { get; }

        public BigDouble Value { get; }

        public IReadOnlyList<string> FlavorIds { get; }

        public DishIconPreviewMode Mode { get; }

        public int? RotationIndex { get; }

        public float VisualSeed { get; }

        public static DishPreviewRequest FromDefinition(
            DishDef dish,
            Sprite sprite = null,
            IReadOnlyList<string> flavorIds = null,
            DishIconPreviewMode mode = DishIconPreviewMode.Card)
        {
            return new DishPreviewRequest(
                dish,
                sprite,
                dish?.Deliciousness ?? 0,
                flavorIds,
                mode,
                null,
                StableVisualSeed(dish));
        }

        public static DishPreviewRequest FromInstance(
            DishInstance dish,
            Sprite sprite = null,
            DishIconPreviewMode mode = DishIconPreviewMode.Warehouse)
        {
            return new DishPreviewRequest(
                dish?.Def,
                sprite,
                DishValueDisplay.CurrentContribution(dish),
                dish?.FlavorIds,
                mode,
                dish?.Placement.RotationIndex,
                dish != null ? dish.Id : StableVisualSeed(dish?.Def));
        }

        private static float StableVisualSeed(DishDef dish)
        {
            return dish != null
                ? (float)(StableHash.Fnv1a64(dish.Id) & 0xFFFFFF)
                : 0f;
        }
    }

    /// <summary>
    /// UI 侧的统一食物图标：显示矩形棋盘、居中的食物和 DishValueBadge 美味值。
    /// 实际世界对象由共享的独立预览场景渲染，本组件只持有自己的 RenderTexture。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class DishIconRenderTexturePreview : MonoBehaviour
    {
        private const float TransformInDuration = 0.14f;
        private const float TransformOutDuration = 0.2f;
        private const float TransformHoldDuration = 0.5f;
        private const float DissolveDuration = 0.85f;
        private const float PrefabGridSize = 3f;
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int BoingId = Shader.PropertyToID("_Boing");
        private static readonly int EdgeClampPointId = Shader.PropertyToID("_EdgeClampPoint");
        private static readonly int DigestProgressId = Shader.PropertyToID("_DigestProgress");
        private static readonly int DigestCenterId = Shader.PropertyToID("_DigestCenter");
        private static readonly int DigestGridSizeId = Shader.PropertyToID("_DigestGridSize");
        private static readonly int DigestSeedId = Shader.PropertyToID("_DigestSeed");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");

        [SerializeField] private RawImage _targetImage;
        [SerializeField] private AspectRatioFitter _aspectRatioFitter;
        [SerializeField] private SpriteRenderer _cellPrefab;
        [SerializeField] private DishPieceView _dishPrefab;
        [SerializeField] private DishValueBadgeView _badgePrefab;
        [SerializeField, Range(32, 256)] private int _pixelsPerCell = 96;
        [SerializeField] private bool _keepBottomAligned = true;
        [SerializeField, HideInInspector] private Vector2 _prefabThreeByThreeSize;

        private RenderTexture _renderTexture;
        private Sequence _transformSequence;
        private Material _transformMaterial;
        private Material _dissolveMaterial;
        private Material _materialBeforeTransform;
        private RectTransform _displaySizeTarget;
        private DishIconPreviewMode _mode;
        private DishDef _boundDish;
        private Sprite _boundSprite;
        private BigDouble _boundValue;
        private DishIconPreviewMode _boundMode;
        private int? _boundRotationIndex;
        private float _boundVisualSeed;
        private readonly List<string> _boundFlavorIds = new();
        private bool _boundFlavorIdsWereNull;
        private bool _hasBinding;
        private bool _requiresLiveRendering;
        private bool _lockDisplayToDefaultFoodCell;

        public RenderTexture CurrentTexture => _renderTexture;

        public RawImage TargetImage
        {
            get
            {
                EnsureRefs();
                return _targetImage;
            }
        }

        public Vector2Int DisplayedGridSize { get; private set; }

        /// <summary>
        /// 复制当前 RT，供源卡被清空后仍需继续显示的购买动画独占使用。
        /// 调用方负责 Release/Destroy 返回的 RenderTexture。
        /// </summary>
        public RenderTexture CopyCurrentTexture()
        {
            if (_renderTexture == null || !_renderTexture.IsCreated())
            {
                return null;
            }

            RenderTextureDescriptor descriptor = _renderTexture.descriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            var copy = new RenderTexture(descriptor)
            {
                name = $"{_renderTexture.name}_PurchaseSnapshot",
                filterMode = _renderTexture.filterMode,
                wrapMode = _renderTexture.wrapMode,
            };
            copy.Create();
            Graphics.Blit(_renderTexture, copy);
            return copy;
        }

        public void SetDisplayLockedToDefaultFoodCell(bool locked)
        {
            _lockDisplayToDefaultFoodCell = locked;
            if (locked)
            {
                ApplyDefaultFoodCellDisplaySize();
            }
            else if (_aspectRatioFitter != null)
            {
                _aspectRatioFitter.enabled = true;
            }
        }

        public void SetRaycastTarget(bool value)
        {
            EnsureRefs();
            if (_targetImage != null)
            {
                _targetImage.raycastTarget = value;
            }
        }

        public void SetRaycastPadding(Vector4 padding)
        {
            EnsureRefs();
            if (_targetImage != null)
            {
                _targetImage.raycastPadding = padding;
            }
        }

        public void SetAlpha(float alpha)
        {
            EnsureRefs();
            if (_targetImage != null)
            {
                Color color = _targetImage.color;
                color.a = Mathf.Clamp01(alpha);
                _targetImage.color = color;
            }
        }

        public static Vector2 DisplaySizeForGrid(
            Vector2 prefabThreeByThreeSize,
            Vector2Int gridSize)
        {
            if (gridSize.x <= 0 || gridSize.y <= 0)
            {
                return prefabThreeByThreeSize;
            }

            return new Vector2(
                prefabThreeByThreeSize.x * gridSize.x / PrefabGridSize,
                prefabThreeByThreeSize.y * gridSize.y / PrefabGridSize);
        }

        public static Vector2Int DisplayedGridSizeFor(
            DishDef dish,
            IReadOnlyList<string> flavorIds = null,
            int? rotationIndexOverride = null)
        {
            if (dish?.Shape == null)
            {
                return Vector2Int.zero;
            }

            IReadOnlyList<string> displayFlavors = flavorIds;
            if (displayFlavors == null && !string.IsNullOrEmpty(dish.FlavorId))
            {
                displayFlavors = new[] { dish.FlavorId };
            }

            int rotationIndex = rotationIndexOverride
                ?? FlavorStainPalette.DisplayRotationIndex(
                    0,
                    displayFlavors);
            DishShape displayShape = dish.Shape.RotatedBy(rotationIndex);
            return new Vector2Int(displayShape.Width, displayShape.Height);
        }

        public void Bind(
            DishDef dish,
            Sprite spriteOverride = null,
            int? deliciousnessOverride = null,
            IReadOnlyList<string> flavorIds = null,
            DishIconPreviewMode mode = DishIconPreviewMode.Card,
            int? rotationIndexOverride = null)
        {
            Bind(new DishPreviewRequest(
                dish,
                spriteOverride,
                deliciousnessOverride ?? dish?.Deliciousness ?? 0,
                flavorIds,
                mode,
                rotationIndexOverride,
                dish != null
                    ? (float)(StableHash.Fnv1a64(dish.Id) & 0xFFFFFF)
                    : 0f));
        }

        public void Bind(DishPreviewRequest request)
        {
            EnsureRefs();
            DishDef dish = request.Dish;
            Sprite sprite = request.Sprite ?? ContentIconLoader.LoadDish(dish);
            _mode = request.Mode;
            DisplayedGridSize = DisplayedGridSizeFor(
                dish,
                request.FlavorIds,
                request.RotationIndex);

            if (dish?.Shape == null || _targetImage == null)
            {
                Hide();
                return;
            }

            if (sprite == null || _cellPrefab == null || _dishPrefab == null || _badgePrefab == null)
            {
                Hide();
                return;
            }

            if (MatchesBinding(request, sprite)
                && _renderTexture != null
                && _renderTexture.IsCreated())
            {
                ApplyTextureToTarget(request.Mode);
                return;
            }

            ReleaseTexture();
            _renderTexture = DishIconPreviewRenderer.Render(
                dish,
                sprite,
                request.Value,
                request.FlavorIds,
                _cellPrefab,
                _dishPrefab,
                _badgePrefab,
                _pixelsPerCell,
                request.Mode,
                request.VisualSeed,
                request.RotationIndex);
            CaptureBinding(request, sprite);
            ApplyTextureToTarget(request.Mode);
        }

        private void OnEnable()
        {
            EnsureRefs();
            CapturePrefabSize();
        }

        private void LateUpdate()
        {
            if (!_requiresLiveRendering
                || !_hasBinding
                || _renderTexture == null
                || !_renderTexture.IsCreated()
                || _targetImage == null
                || !_targetImage.enabled)
            {
                return;
            }

            IReadOnlyList<string> flavorIds = _boundFlavorIdsWereNull
                ? null
                : _boundFlavorIds;
            DishIconPreviewRenderer.RenderInto(
                _renderTexture,
                _boundDish,
                _boundSprite,
                _boundValue,
                flavorIds,
                _cellPrefab,
                _dishPrefab,
                _badgePrefab,
                _pixelsPerCell,
                _boundMode,
                _boundVisualSeed,
                _boundRotationIndex);
        }

        public void PlayTransformTo(
            DishDef dish,
            IReadOnlyList<string> flavorIds,
            int? deliciousnessOverride,
            Action onComplete)
        {
            EnsureRefs();
            KillTransformSequence();
            if (dish?.Shape == null || _targetImage == null)
            {
                Bind(
                    dish,
                    deliciousnessOverride: deliciousnessOverride,
                    flavorIds: flavorIds,
                    mode: _mode);
                onComplete?.Invoke();
                return;
            }

            if (!BeginTransformMaterial())
            {
                Transform target = transform;
                _transformSequence = DOTween.Sequence()
                    .Append(target.DOPunchScale(
                        Vector3.one * 0.08f,
                        0.24f,
                        vibrato: 6,
                        elasticity: 0.6f))
                    .InsertCallback(TransformInDuration, () => Bind(
                        dish,
                        deliciousnessOverride: deliciousnessOverride,
                        flavorIds: flavorIds,
                        mode: _mode))
                    .AppendInterval(TransformHoldDuration)
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() =>
                    {
                        _transformSequence = null;
                        onComplete?.Invoke();
                    });
                return;
            }

            ApplyTransformEffect(0f);
            _transformSequence = DOTween.Sequence()
                .Append(DOTween.To(
                        () => 0f,
                        ApplyTransformEffect,
                        1f,
                        TransformInDuration)
                    .SetEase(Ease.OutQuad))
                .AppendCallback(() =>
                {
                    Bind(
                        dish,
                        deliciousnessOverride: deliciousnessOverride,
                        flavorIds: flavorIds,
                        mode: _mode);
                    ApplyTransformEffect(1f);
                })
                .Append(DOTween.To(
                        () => 1f,
                        ApplyTransformEffect,
                        0f,
                        TransformOutDuration)
                    .SetEase(Ease.InOutQuad))
                .AppendInterval(TransformHoldDuration)
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _transformSequence = null;
                    EndTransformMaterial();
                    onComplete?.Invoke();
                });
        }

        public void PlayDissolve(Action onComplete)
        {
            EnsureRefs();
            KillTransformSequence();
            if (_targetImage == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!BeginDissolveMaterial())
            {
                Color start = _targetImage.color;
                _transformSequence = DOTween.Sequence()
                    .Append(DOTween.To(
                            () => 0f,
                            value =>
                            {
                                Color faded = start;
                                faded.a = start.a * (1f - Mathf.SmoothStep(0f, 1f, value));
                                _targetImage.color = faded;
                            },
                            1f,
                            DissolveDuration)
                        .SetEase(Ease.Linear))
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() =>
                    {
                        _transformSequence = null;
                        Color faded = start;
                        faded.a = 0f;
                        _targetImage.color = faded;
                        onComplete?.Invoke();
                    });
                return;
            }

            ApplyDissolveProgress(0f);
            _transformSequence = DOTween.Sequence()
                .Append(DOTween.To(
                        () => 0f,
                        ApplyDissolveProgress,
                        1f,
                        DissolveDuration)
                    .SetEase(Ease.Linear))
                .SetUpdate(true)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _transformSequence = null;
                    ApplyDissolveProgress(1f);
                    onComplete?.Invoke();
                });
        }

        public void Hide()
        {
            EnsureRefs();
            KillTransformSequence();
            ReleaseTexture();
            ClearBinding();
            if (_targetImage != null)
            {
                _targetImage.texture = null;
                _targetImage.enabled = false;
            }

            if (_mode == DishIconPreviewMode.Card)
            {
                RestorePrefabSize();
            }
        }

        private void OnDestroy()
        {
            KillTransformSequence();
            ReleaseTexture();
            if (_transformMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_transformMaterial);
                }
                else
                {
                    DestroyImmediate(_transformMaterial);
                }

                _transformMaterial = null;
            }

            if (_dissolveMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_dissolveMaterial);
                }
                else
                {
                    DestroyImmediate(_dissolveMaterial);
                }

                _dissolveMaterial = null;
            }
        }

        private void KillTransformSequence()
        {
            if (_transformSequence != null)
            {
                _transformSequence.Kill();
                _transformSequence = null;
            }

            EndTransformMaterial();
            EndDissolveMaterial();
        }

        private bool BeginTransformMaterial()
        {
            Material source = SpriteRenderStyle.SpriteTransformMaterial;
            if (_targetImage == null || source == null)
            {
                return false;
            }

            if (_transformMaterial == null)
            {
                _transformMaterial = new Material(source)
                {
                    name = $"{name}_SpriteTransform",
                    hideFlags = HideFlags.DontSave,
                };
            }

            _materialBeforeTransform = _targetImage.material;
            _targetImage.material = _transformMaterial;
            return true;
        }

        private void ApplyTransformEffect(float amount)
        {
            if (_transformMaterial == null)
            {
                return;
            }

            float t = Mathf.Clamp01(amount);
            _transformMaterial.SetFloat(BrightnessId, t);
            _transformMaterial.SetVector(BoingId, new Vector4(0.2f * t, -0.14f * t, 0f, 0f));
            _transformMaterial.SetVector(EdgeClampPointId, new Vector4(0.24f, 0.24f, 0f, 0f));
        }

        private void EndTransformMaterial()
        {
            if (_targetImage != null && _targetImage.material == _transformMaterial)
            {
                _targetImage.material = _materialBeforeTransform;
            }

            _materialBeforeTransform = null;
            ApplyTransformEffect(0f);
        }

        private bool BeginDissolveMaterial()
        {
            Material source = SpriteRenderStyle.DigestDissolveMaterial;
            if (_targetImage == null || source == null)
            {
                return false;
            }

            if (_dissolveMaterial == null)
            {
                _dissolveMaterial = new Material(source)
                {
                    name = $"{name}_DigestDissolve",
                    hideFlags = HideFlags.DontSave,
                };
            }

            _materialBeforeTransform = _targetImage.material;
            _targetImage.material = _dissolveMaterial;
            _dissolveMaterial.SetVector(SpriteUvRectId, new Vector4(0f, 0f, 1f, 1f));
            _dissolveMaterial.SetVector(DigestCenterId, new Vector2(0.5f, 0.5f));
            _dissolveMaterial.SetVector(
                DigestGridSizeId,
                new Vector2(
                    Mathf.Max(1, DisplayedGridSize.x),
                    Mathf.Max(1, DisplayedGridSize.y)));
            _dissolveMaterial.SetFloat(DigestSeedId, _boundVisualSeed);
            if (_targetImage.texture != null)
            {
                _dissolveMaterial.mainTexture = _targetImage.texture;
            }

            return true;
        }

        private void ApplyDissolveProgress(float amount)
        {
            if (_dissolveMaterial == null)
            {
                return;
            }

            _dissolveMaterial.SetFloat(DigestProgressId, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount)));
        }

        private void EndDissolveMaterial()
        {
            if (_targetImage != null && _targetImage.material == _dissolveMaterial)
            {
                _targetImage.material = _materialBeforeTransform;
            }

            _materialBeforeTransform = null;
            ApplyDissolveProgress(0f);
        }

        private void EnsureRefs()
        {
            _targetImage ??= GetComponent<RawImage>();
            _aspectRatioFitter ??= GetComponent<AspectRatioFitter>();
            if (_displaySizeTarget == null)
            {
                _displaySizeTarget =
                    transform.parent as RectTransform ??
                    transform as RectTransform;
            }
        }

        private void CapturePrefabSize()
        {
            if (_prefabThreeByThreeSize.x > 0f
                && _prefabThreeByThreeSize.y > 0f)
            {
                return;
            }

            EnsureRefs();
            if (_displaySizeTarget == null)
            {
                return;
            }

            Vector2 size = _displaySizeTarget.rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                size = new Vector2(
                    Mathf.Abs(_displaySizeTarget.sizeDelta.x),
                    Mathf.Abs(_displaySizeTarget.sizeDelta.y));
            }

            if (size.x > 0f && size.y > 0f)
            {
                _prefabThreeByThreeSize = size;
            }
        }

        private void ApplyDisplaySize(Vector2Int gridSize)
        {
            CapturePrefabSize();
            if (_displaySizeTarget == null
                || _prefabThreeByThreeSize.x <= 0f
                || _prefabThreeByThreeSize.y <= 0f)
            {
                return;
            }

            Vector2 displaySize = DisplaySizeForGrid(
                _prefabThreeByThreeSize,
                gridSize);
            SetDisplaySize(displaySize);
        }

        private void RestorePrefabSize()
        {
            if (_displaySizeTarget == null
                || _prefabThreeByThreeSize.x <= 0f
                || _prefabThreeByThreeSize.y <= 0f)
            {
                return;
            }

            SetDisplaySize(_prefabThreeByThreeSize);
        }

        private void SetDisplaySize(Vector2 displaySize)
        {
            if (_keepBottomAligned)
            {
                SetDisplaySizeKeepingBottom(displaySize);
                return;
            }

            SetDisplaySizeKeepingCenter(displaySize);
        }

        private void SetDisplaySizeKeepingBottom(Vector2 displaySize)
        {
            Vector3 bottomCenterBefore = _displaySizeTarget.TransformPoint(
                new Vector3(
                    _displaySizeTarget.rect.center.x,
                    _displaySizeTarget.rect.yMin,
                    0f));
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                displaySize.x);
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                displaySize.y);
            Vector3 bottomCenterAfter = _displaySizeTarget.TransformPoint(
                new Vector3(
                    _displaySizeTarget.rect.center.x,
                    _displaySizeTarget.rect.yMin,
                    0f));
            _displaySizeTarget.position += bottomCenterBefore - bottomCenterAfter;
            LayoutRebuilder.MarkLayoutForRebuild(_displaySizeTarget);
        }

        private void SetDisplaySizeKeepingCenter(Vector2 displaySize)
        {
            Vector3 centerBefore = _displaySizeTarget.TransformPoint(
                _displaySizeTarget.rect.center);
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                displaySize.x);
            _displaySizeTarget.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                displaySize.y);
            Vector3 centerAfter = _displaySizeTarget.TransformPoint(
                _displaySizeTarget.rect.center);
            _displaySizeTarget.position += centerBefore - centerAfter;
            LayoutRebuilder.MarkLayoutForRebuild(_displaySizeTarget);
        }

        private void ReleaseTexture()
        {
            if (_renderTexture == null)
            {
                return;
            }

            if (_targetImage != null && _targetImage.texture == _renderTexture)
            {
                _targetImage.texture = null;
            }

            _renderTexture.Release();
            if (Application.isPlaying)
            {
                Destroy(_renderTexture);
            }
            else
            {
                DestroyImmediate(_renderTexture);
            }

            _renderTexture = null;
        }

        private bool MatchesBinding(DishPreviewRequest request, Sprite resolvedSprite)
        {
            if (!_hasBinding
                || !ReferenceEquals(_boundDish, request.Dish)
                || !ReferenceEquals(_boundSprite, resolvedSprite)
                || _boundValue != request.Value
                || _boundMode != request.Mode
                || _boundRotationIndex != request.RotationIndex
                || !Mathf.Approximately(_boundVisualSeed, request.VisualSeed)
                || _boundFlavorIdsWereNull != (request.FlavorIds == null))
            {
                return false;
            }

            int flavorCount = request.FlavorIds?.Count ?? 0;
            if (_boundFlavorIds.Count != flavorCount)
            {
                return false;
            }

            for (int i = 0; i < flavorCount; i++)
            {
                if (!string.Equals(_boundFlavorIds[i], request.FlavorIds[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void CaptureBinding(DishPreviewRequest request, Sprite resolvedSprite)
        {
            _boundDish = request.Dish;
            _boundSprite = resolvedSprite;
            _boundValue = request.Value;
            _boundMode = request.Mode;
            _boundRotationIndex = request.RotationIndex;
            _boundVisualSeed = request.VisualSeed;
            _boundFlavorIdsWereNull = request.FlavorIds == null;
            _boundFlavorIds.Clear();
            if (request.FlavorIds != null)
            {
                for (int i = 0; i < request.FlavorIds.Count; i++)
                {
                    _boundFlavorIds.Add(request.FlavorIds[i]);
                }
            }

            _hasBinding = _renderTexture != null;
            _requiresLiveRendering = HasVisibleFlavor(
                request.Dish,
                request.FlavorIds);
        }

        private void ClearBinding()
        {
            _boundDish = null;
            _boundSprite = null;
            _boundValue = 0;
            _boundMode = default;
            _boundRotationIndex = null;
            _boundVisualSeed = 0f;
            _boundFlavorIdsWereNull = false;
            _boundFlavorIds.Clear();
            _hasBinding = false;
            _requiresLiveRendering = false;
        }

        private static bool HasVisibleFlavor(
            DishDef dish,
            IReadOnlyList<string> flavorIds)
        {
            if (flavorIds != null)
            {
                return FlavorOrganicVisual.HasVisibleFlavor(flavorIds);
            }

            return FlavorVisualCatalog.TryResolve(dish?.FlavorId, out _);
        }

        private void ApplyTextureToTarget(DishIconPreviewMode mode)
        {
            _targetImage.texture = _renderTexture;
            _targetImage.color = Color.white;
            _targetImage.enabled = _renderTexture != null;
            gameObject.SetActive(_renderTexture != null);
            if (_renderTexture == null)
            {
                return;
            }

            Vector2Int renderedGridSize = new(
                _renderTexture.width / _pixelsPerCell,
                _renderTexture.height / _pixelsPerCell);
            if (_lockDisplayToDefaultFoodCell)
            {
                ApplyDefaultFoodCellDisplaySize();
            }
            else if (mode == DishIconPreviewMode.Card)
            {
                ApplyDisplaySize(renderedGridSize);
            }

            if (_aspectRatioFitter != null && !_lockDisplayToDefaultFoodCell)
            {
                _aspectRatioFitter.enabled = true;
                _aspectRatioFitter.aspectMode =
                    AspectRatioFitter.AspectMode.FitInParent;
                _aspectRatioFitter.aspectRatio =
                    (float)_renderTexture.width / _renderTexture.height;
            }
        }

        private void ApplyDefaultFoodCellDisplaySize()
        {
            EnsureRefs();
            var self = transform as RectTransform;
            if (self == null)
            {
                return;
            }

            _displaySizeTarget = self;
            if (_aspectRatioFitter != null)
            {
                _aspectRatioFitter.enabled = false;
            }

            Vector2Int grid = DisplayedGridSize.x > 0 && DisplayedGridSize.y > 0
                ? DisplayedGridSize
                : Vector2Int.one;
            Vector2 cell = DiningTableLayout.CanvasPixelsForWorldSize(
                Vector2.one * DiningTableLayout.DefaultFoodCellSize,
                Camera.main,
                GetComponentInParent<Canvas>());
            var parent = self.parent as RectTransform;
            if (parent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            }

            Vector2 displaySize = DiningTableLayout.FitInside(
                DiningTableLayout.DefaultFoodCanvasSize(grid, cell),
                DiningTableLayout.RectSize(parent));
            self.anchoredPosition = Vector2.zero;
            self.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, displaySize.x);
            self.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, displaySize.y);
            LayoutRebuilder.MarkLayoutForRebuild(self);
        }
    }
}
