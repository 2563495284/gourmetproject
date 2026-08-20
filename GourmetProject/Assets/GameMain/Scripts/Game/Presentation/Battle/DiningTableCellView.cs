using System;
using DG.Tweening;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Visual;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 餐桌格表现：Prefab 根节点表示真实网格，方形桌面由单个 PlateVisual Renderer 承载。
    /// 固定视觉层级、相对位置、缩放和碰撞范围均由 Prefab 作者配置；运行时只喂数据与状态。
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class DiningTableCellView : MonoBehaviour
    {
        private const int RowSortingStride = 2;
        private const float SettlementOrderPulseDuration = 0.24f;
        private const float SettlementOrderPulseRiseDuration = 0.09f;
        private const float SettlementOrderPulseBrightness = 0.55f;
        private const float SettlementOrderPulseScale = 1.06f;
        private static readonly Color SettlementOrderPulseTint = new Color(1f, 0.72f, 0.28f, 1f);
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int BoingId = Shader.PropertyToID("_Boing");
        private static readonly int EdgeClampPointId = Shader.PropertyToID("_EdgeClampPoint");

        [Tooltip("方形桌面视觉根节点。其局部位置/缩放由设计师在 Prefab 中调整，运行时不会重建或重排。")]
        [SerializeField] private Transform _visualRoot;

        [Tooltip("唯一的方形桌面渲染体。红/绿/黄格子反馈直接改变此 Renderer 的颜色。")]
        [SerializeField] private SpriteRenderer _plateRenderer;

        [Tooltip("真实桌面网格的点击命中范围，与方形 PlateVisual 对齐。")]
        [SerializeField] private BoxCollider2D _collider;

        private GridPos _position;
        private Action<GridPos> _clicked;
        private Action<DiningTableCellView> _hoverEntered;
        private Action<DiningTableCellView> _hoverExited;
        private MaterialPropertyBlock _propertyBlock;
        private DiningTableCellSprites _configuredSprites;
        private float _configuredSize;
        private Vector3 _visualBaseLocalScale = Vector3.one;
        private Color _baseColor = Color.white;
        private Color _plateFeedbackColor = Color.white;
        private bool _visualScaleCaptured;
        private bool _plateFeedbackActive;
        private bool _hovered;
        private Sequence _transformSequence;
        private Action _transformOnComplete;

        public GridPos Position => _position;

        public Bounds WorldBounds
        {
            get
            {
                EnsureRefs();
                if (_collider != null)
                {
                    // Collider2D.bounds 要等物理 Transform 同步后才可靠；候选托盘会在同一帧内
                    // 配置位置、缩放和旋转并立即建立命中区，因此直接从局部碰撞框四角计算世界 AABB。
                    Vector2 halfSize = _collider.size * 0.5f;
                    Vector2 offset = _collider.offset;
                    Vector3 bottomLeft = transform.TransformPoint(offset + new Vector2(-halfSize.x, -halfSize.y));
                    Vector3 topLeft = transform.TransformPoint(offset + new Vector2(-halfSize.x, halfSize.y));
                    Vector3 topRight = transform.TransformPoint(offset + new Vector2(halfSize.x, halfSize.y));
                    Vector3 bottomRight = transform.TransformPoint(offset + new Vector2(halfSize.x, -halfSize.y));
                    var bounds = new Bounds(bottomLeft, Vector3.zero);
                    bounds.Encapsulate(topLeft);
                    bounds.Encapsulate(topRight);
                    bounds.Encapsulate(bottomRight);
                    return bounds;
                }

                if (_plateRenderer != null && _plateRenderer.sprite != null)
                {
                    return _plateRenderer.bounds;
                }

                return new Bounds(transform.position, Vector3.one);
            }
        }

        /// <summary>配置一个由 prefab 实例化出来的格子；固定结构与相对摆位全部来自 prefab。</summary>
        public void Configure(
            GridPos position,
            Vector3 localPosition,
            float size,
            DiningTableCellSprites sprites,
            Action<GridPos> clicked)
        {
            EnsureRefs();

            gameObject.name = $"Cell_{position.X}_{position.Y}";
            transform.localPosition = localPosition;
            _position = position;
            _clicked = clicked;

            SetSprites(sprites, size);
        }

        public void SetSprites(DiningTableCellSprites sprites, float size)
        {
            EnsureRefs();
            if (!sprites.IsValid)
            {
                throw new InvalidOperationException(
                    $"{nameof(DiningTableCellView)} 收到无效的餐桌格 Sprite。");
            }

            bool spritesChanged = _configuredSprites.Plate != sprites.Plate;
            bool sizeChanged = !Mathf.Approximately(_configuredSize, size);
            if (sizeChanged)
            {
                float safeSize = Mathf.Max(0.0001f, size);
                transform.localScale = new Vector3(safeSize, safeSize, 1f);
                _visualRoot.localScale = _visualBaseLocalScale;
            }

            bool preserveTransformMaterial = IsTransformMaterialActive();
            if (spritesChanged)
            {
                _plateRenderer.sprite = sprites.Plate;
                if (!preserveTransformMaterial)
                {
                    RestoreUnlitMaterials();
                }
            }

            _configuredSprites = sprites;
            _configuredSize = size;
            ApplyColors();
            ApplySorting(BattleSorting.DiningTable, 0);
        }

        public void SetHoverCallbacks(Action<DiningTableCellView> entered, Action<DiningTableCellView> exited)
        {
            _hoverEntered = entered;
            _hoverExited = exited;
        }

        /// <summary>设置格子的基础颜色/透明度；反馈色会保留基础 Alpha。</summary>
        public void SetColor(Color color)
        {
            EnsureRefs();
            _baseColor = color;
            ApplyColors();
        }

        /// <summary>仅让盘子使用红/绿/黄反馈乘色，不切换外发光材质。</summary>
        public void SetPlateFeedbackColor(Color color)
        {
            EnsureRefs();
            _plateFeedbackColor = color;
            _plateFeedbackActive = true;
            ApplyColors();
        }

        public void ClearPlateFeedbackColor()
        {
            EnsureRefs();
            _plateFeedbackActive = false;
            ApplyColors();
        }

        public void SetDebuffed(bool debuffed)
        {
            EnsureRefs();
            SetRendererDebuffed(_plateRenderer, debuffed);
        }

        /// <summary>
        /// 经营挑战入场时的单格结算顺序提示。延迟只控制接力节拍；亮度、轻微形变和缩放在
        /// 完成、取消或对象禁用时都会恢复，避免把临时材质状态带进正常交互。
        /// </summary>
        internal void PlaySettlementOrderHintPulse(float delaySeconds)
        {
            EnsureRefs();
            KillTransformSequence(resetMaterial: true);

            Vector3 baseScale = _visualBaseLocalScale;
            Color baseRendererColor = _plateRenderer.color;
            Color peakRendererColor = new Color(
                SettlementOrderPulseTint.r,
                SettlementOrderPulseTint.g,
                SettlementOrderPulseTint.b,
                baseRendererColor.a);
            if (SpriteRenderStyle.SpriteTransformMaterial == null)
            {
                _transformSequence = DOTween.Sequence()
                    .SetDelay(Mathf.Max(0f, delaySeconds))
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .Append(_visualRoot.DOScale(baseScale * SettlementOrderPulseScale, SettlementOrderPulseRiseDuration)
                        .SetEase(Ease.OutCubic))
                    .Join(_plateRenderer.DOColor(peakRendererColor, SettlementOrderPulseRiseDuration)
                        .SetEase(Ease.OutCubic))
                    .Append(_visualRoot.DOScale(
                            baseScale,
                            SettlementOrderPulseDuration - SettlementOrderPulseRiseDuration)
                        .SetEase(Ease.InOutSine))
                    .Join(_plateRenderer.DOColor(
                            baseRendererColor,
                            SettlementOrderPulseDuration - SettlementOrderPulseRiseDuration)
                        .SetEase(Ease.InOutSine));
                BindTransformSequenceCleanup(_transformSequence);
                return;
            }

            SpriteRenderStyle.ApplyTransformMaterial(_plateRenderer);
            ApplyTransformEffect(0f);
            _transformSequence = DOTween.Sequence()
                .SetDelay(Mathf.Max(0f, delaySeconds))
                .SetUpdate(true)
                .SetLink(gameObject)
                .Append(DOTween.To(
                        () => 0f,
                        ApplyTransformEffect,
                        SettlementOrderPulseBrightness,
                        SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.OutCubic))
                .Join(_visualRoot.DOScale(baseScale * SettlementOrderPulseScale, SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.OutCubic))
                .Join(_plateRenderer.DOColor(peakRendererColor, SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.OutCubic))
                .Append(DOTween.To(
                        () => SettlementOrderPulseBrightness,
                        ApplyTransformEffect,
                        0f,
                        SettlementOrderPulseDuration - SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.InOutSine))
                .Join(_visualRoot.DOScale(
                        baseScale,
                        SettlementOrderPulseDuration - SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.InOutSine))
                .Join(_plateRenderer.DOColor(
                        baseRendererColor,
                        SettlementOrderPulseDuration - SettlementOrderPulseRiseDuration)
                    .SetEase(Ease.InOutSine));
            BindTransformSequenceCleanup(_transformSequence);
        }

        internal void CancelSettlementOrderHintPulse()
        {
            KillTransformSequence(resetMaterial: true);
        }

        /// <summary>调整层内基准序号；方形桌面仍会叠加逻辑行深度。</summary>
        public void SetSortingOrder(int order)
        {
            EnsureRefs();
            ApplySorting(_plateRenderer.sortingLayerName, order);
        }

        public void SetSorting(string layer, int order)
        {
            EnsureRefs();
            ApplySorting(layer, order);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            EnsureRefs();
            _collider.enabled = enabled;
            if (!enabled)
            {
                _clicked = null;
                _hoverEntered = null;
                _hoverExited = null;
                SetHovered(false);
            }
        }

        private void ApplyColors()
        {
            _plateRenderer.color = _plateFeedbackActive
                ? new Color(
                    _plateFeedbackColor.r,
                    _plateFeedbackColor.g,
                    _plateFeedbackColor.b,
                    _baseColor.a * _plateFeedbackColor.a)
                : _baseColor;
        }

        private void ApplySorting(string layer, int baseOrder)
        {
            int plateOrder = baseOrder + _position.Y * RowSortingStride + 1;
            BattleSorting.Apply(_plateRenderer, layer, plateOrder);
        }

        private static void SetRendererDebuffed(SpriteRenderer renderer, bool debuffed)
        {
            if (debuffed)
            {
                renderer.SetPropertyBlock(null);
                DebuffVisualStyle.ApplyToSprite(renderer);
            }
            else if (DebuffVisualStyle.IsAppliedToSprite(renderer))
            {
                DebuffVisualStyle.ClearSprite(renderer);
            }
        }

        private void ApplyTransformEffect(float amount)
        {
            float t = Mathf.Clamp01(amount);
            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.Clear();
            _propertyBlock.SetFloat(BrightnessId, t);
            _propertyBlock.SetVector(BoingId, new Vector4(0.2f * t, -0.14f * t, 0f, 0f));
            _propertyBlock.SetVector(EdgeClampPointId, new Vector4(0.24f, 0.24f, 0f, 0f));
            _plateRenderer.SetPropertyBlock(_propertyBlock);
        }

        private bool IsTransformMaterialActive()
        {
            return _transformSequence != null
                && SpriteRenderStyle.SpriteTransformMaterial != null
                && _plateRenderer.sharedMaterial == SpriteRenderStyle.SpriteTransformMaterial;
        }

        private void BindTransformSequenceCleanup(Sequence sequence)
        {
            sequence.OnComplete(() => CompleteTransform(sequence));
            sequence.OnKill(() => CompleteTransform(sequence));
        }

        private void CompleteTransform(Sequence sequence)
        {
            if (_transformSequence != sequence)
            {
                return;
            }

            _transformSequence = null;
            if (_visualScaleCaptured)
            {
                _visualRoot.localScale = _visualBaseLocalScale;
            }

            RestoreUnlitMaterials();
            ApplyColors();
            Action callback = _transformOnComplete;
            _transformOnComplete = null;
            callback?.Invoke();
        }

        private void KillTransformSequence(bool resetMaterial)
        {
            if (_transformSequence != null)
            {
                Sequence sequence = _transformSequence;
                _transformSequence = null;
                sequence.Kill();
            }

            if (_visualScaleCaptured)
            {
                _visualRoot.localScale = _visualBaseLocalScale;
            }

            if (resetMaterial)
            {
                RestoreUnlitMaterials();
                ApplyColors();
            }

            Action callback = _transformOnComplete;
            _transformOnComplete = null;
            callback?.Invoke();
        }

        private void RestoreUnlitMaterials()
        {
            _plateRenderer.SetPropertyBlock(null);
            SpriteRenderStyle.ApplyUnlitMaterial(_plateRenderer);
        }

        private void Awake()
        {
            EnsureRefs();
        }

        /// <summary>固定视觉结构必须由 Prefab 完整绑定；缺失时明确失败，禁止运行时补节点或补组件。</summary>
        private void EnsureRefs()
        {
            if (_visualRoot == null || _plateRenderer == null || _collider == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(DiningTableCellView)} 的 Prefab 绑定不完整：必须配置 " +
                    $"{nameof(_visualRoot)}、{nameof(_plateRenderer)}、{nameof(_collider)}。");
            }

            if (!_visualScaleCaptured)
            {
                _visualBaseLocalScale = _visualRoot.localScale;
                _visualScaleCaptured = true;
            }
        }

        private void OnMouseDown()
        {
            if (WorldInput.PointerOverUi)
            {
                return;
            }

            _clicked?.Invoke(_position);
        }

        private void Update()
        {
            UpdateHover();
        }

        private void UpdateHover()
        {
            EnsureRefs();
            if (WorldInput.PointerOverUi)
            {
                SetHovered(false);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                SetHovered(false);
                return;
            }

            SetHovered(_collider.OverlapPoint(WorldInput.MouseWorld(cam)));
        }

        private void SetHovered(bool hovered)
        {
            if (_hovered == hovered)
            {
                return;
            }

            _hovered = hovered;
            if (_hovered)
            {
                _hoverEntered?.Invoke(this);
            }
            else
            {
                _hoverExited?.Invoke(this);
            }
        }

        private void OnDisable()
        {
            _plateFeedbackActive = false;
            KillTransformSequence(resetMaterial: true);
            SetHovered(false);
        }
    }
}
