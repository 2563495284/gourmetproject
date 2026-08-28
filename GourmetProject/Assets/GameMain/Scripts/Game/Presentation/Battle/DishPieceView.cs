using System;
using System.Collections.Generic;
using System.Threading;
using DG.Tweening;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Visual;
using TMPro;
using UnityEngine.UI;

namespace GourmetProject.Game.Presentation.Battle
{
    public enum SettlementDishFeedbackKind
    {
        None = 0,
        DishBase = 1,
        GenericSkillTriggered = 2,
        SweetTransferSkillTriggered = 3,
        CopiedSkillTriggered = 4,
        CopySkillTriggered = 5,
        PassiveFlatBonus = 6,
        PassiveMultiplier = 7,
        PassiveMultiplierAdd = 8,
        ActiveFlatBonus = 9,
        ActiveMultiplier = 10,
        ActiveMultiplierAdd = 11,
        GenericValueChanged = 12,
        TriggerSweetTransferActivatorPulse = 13,
        SweetTransferResult = 14,
        SweetTransferExecutor = 15,
        SweetTransferFailed = 16,
        PermanentFlatBonus = 17,
        CountAsChanged = 18,
        TemporaryCategoryApplied = 19,
        DishChapterStarted = 20,
        DishChapterCompleted = 21,
        CakeLayerFlatBurst = 22,
        CakeLayerMultiplierAddBurst = 23,
        CakeLayerMultiplierBurst = 24,
    }

    public readonly struct DishGrabVisualSnapshot
    {
        public DishGrabVisualSnapshot(
            Sprite sprite,
            Color color,
            Vector2 screenCenter,
            Vector2 screenSize,
            float screenRotationDegrees,
            bool flipX,
            bool flipY,
            IReadOnlyList<string> flavorIds,
            float flavorSeed,
            float flavorIntensity)
        {
            Sprite = sprite;
            Color = color;
            ScreenCenter = screenCenter;
            ScreenSize = screenSize;
            ScreenRotationDegrees = screenRotationDegrees;
            FlipX = flipX;
            FlipY = flipY;
            FlavorIds = flavorIds;
            FlavorSeed = flavorSeed;
            FlavorIntensity = flavorIntensity;
        }

        public Sprite Sprite { get; }
        public Color Color { get; }
        public Vector2 ScreenCenter { get; }
        public Vector2 ScreenSize { get; }
        public float ScreenRotationDegrees { get; }
        public bool FlipX { get; }
        public bool FlipY { get; }
        public IReadOnlyList<string> FlavorIds { get; }
        public float FlavorSeed { get; }
        public float FlavorIntensity { get; }
    }

    /// <summary>
    /// 已摆放食物表现：固定结构（接触阴影 + 食物本体 + 碰撞盒）预拼在 prefab 上，由 <see cref="BuildPlaced"/> 喂数据。
    /// sprite/缩放/旋转/碰撞尺寸随形状(1x1/2x1/L/T...)与朝向变化，必须运行时计算（见 dish-footprint-sprite 规则）。
    /// 阴影一律复用食物 Alpha 轮廓做绘本式接触影（见 battle-fake-shadow 规则），全程 Unlit 平涂，不依赖 Light2D。
    /// </summary>
    public sealed class DishPieceView : MonoBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int OuterAlphaId = Shader.PropertyToID("_OuterAlpha");
        private static readonly int InnerAlphaId = Shader.PropertyToID("_InnerAlpha");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int PulseAmplitudeId = Shader.PropertyToID("_PulseAmplitude");
        private static readonly int PulseFrequencyId = Shader.PropertyToID("_PulseFrequency");
        private static readonly int UvInflateId = Shader.PropertyToID("_UvInflate");
        private static readonly int SpriteUvRectId = Shader.PropertyToID("_SpriteUvRect");
        private static readonly int DigestProgressId = Shader.PropertyToID("_DigestProgress");
        private static readonly int DigestCenterId = Shader.PropertyToID("_DigestCenter");
        private static readonly int DigestGridSizeId = Shader.PropertyToID("_DigestGridSize");
        private static readonly int DigestSeedId = Shader.PropertyToID("_DigestSeed");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int BoingId = Shader.PropertyToID("_Boing");
        private static readonly int EdgeClampPointId = Shader.PropertyToID("_EdgeClampPoint");

        [Header("接触阴影：贴桌态（复用食物 Alpha 轮廓，偏移按单格尺寸取比例）")]
        [SerializeField] private float _shadowBaseAlpha = 0.28f;
        [SerializeField] private float _shadowGroundScale = 1.02f;
        [SerializeField] private float _shadowGroundDrop = 0.04f;
        [SerializeField] private float _shadowGroundSide = 0.015f;

        [Header("接触阴影：举高态（越高越淡，低透明轮廓层轻微外扩）")]
        [Tooltip("阴影达到最大外扩/淡化的参考高度（按单格尺寸倍数，适配不同餐桌缩放）。")]
        [SerializeField] private float _shadowLiftRefCells = 1.5f;
        [Tooltip("锐利核心层在最高处的放大倍数。")]
        [SerializeField] private float _coreGrow = 1.08f;
        [Tooltip("锐利核心层在最高处的透明度乘子（越小越淡）。")]
        [SerializeField] private float _coreFadeWhenHigh = 0.25f;
        [Tooltip("低透明外扩轮廓层在最高处的放大倍数。")]
        [SerializeField] private float _haloGrow = 1.12f;
        [Tooltip("低透明外扩轮廓层在最高处的透明度（绝对值，贴桌时为 0）。")]
        [SerializeField] private float _haloAlphaWhenHigh = 0.08f;

        [Header("拖拽悬浮（本体中心始终跟随鼠标，阴影只负责制造离桌感）")]
        [SerializeField] private float _dragVisualScale = 1.15f;
        [SerializeField] private float _dragShadowSideCells = 0.06f;
        [SerializeField] private float _dragShadowDropCells = 0.10f;
        [SerializeField] private float _dragShadowCoreScale = 1.03f;
        [SerializeField] private float _dragShadowCoreAlpha = 0.20f;
        [SerializeField] private float _dragShadowHaloScale = 1.07f;
        [SerializeField] private float _dragShadowHaloAlpha = 0.045f;

        [Header("固定结构（prefab 预拼，运行时引用）")]
        [Tooltip("食物本体渲染体（子物体 Sprite 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [Tooltip("食物本体动画枢轴（子物体 VisualPivot）。多格食物的缩放/晃动绕这里执行，根节点保持贴格。")]
        [SerializeField] private Transform _visualPivot;
        [Tooltip("脚下接触阴影锐利核心层（子物体 Shadow 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _shadowRenderer;
        [Tooltip("脚下接触阴影弥散光晕层（子物体 ShadowHalo 上的 SpriteRenderer），高空时显现做虚化。")]
        [SerializeField] private SpriteRenderer _shadowHaloRenderer;
        [Tooltip("点击命中碰撞盒（prefab 根节点上的 BoxCollider2D）。")]
        [SerializeField] private BoxCollider2D _collider;
        [Tooltip("放置合法性发光层（子物体 PlacementGlow 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _placementGlow;
        [Tooltip("技能实际目标发光层（子物体 ScopeTargetGlow 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _scopeTargetGlow;
        [Tooltip("常驻美味值标签的独立表现器。")]
        [SerializeField] private DishPieceValueBadgePresenter _dishValueBadgePresenter;

        [Header("落定反馈（仅作用于本体视觉枢轴，不影响格子锚点/碰撞盒）")]
        [SerializeField] private bool _useOccupiedCentroidPivot = true;
        [SerializeField] private float _landPunchScale = 1.12f;
        [SerializeField] private float _landPunchDuration = 0.16f;
        [SerializeField] private float _landWobbleDegrees = 4f;
        [SerializeField] private float _landWobbleDuration = 0.18f;
        [SerializeField] private float _landWobbleCycles = 1.5f;

        [Header("Scope 受影响反馈（仅作用于本体视觉枢轴）")]
        [Tooltip("横向抖动幅度，按单格尺寸取比例。")]
        [SerializeField] private float _scopeAffectedShakeCells = 0.045f;
        [SerializeField] private float _scopeAffectedShakeDuration = 0.18f;
        [SerializeField] private float _scopeAffectedShakeCycles = 2.25f;

        [Header("上菜落格砰反馈（仅缩放）")]
        [SerializeField] private float _serveLandImpactScale = 1.02f;
        [SerializeField] private float _serveLandImpactDuration = 0.14f;

        [Header("Boss 开胃菜：消化溶解")]
        [Tooltip("落地后从食物外圈向占格视觉中心溶解的时长。")]
        [SerializeField] private float _digestDissolveDuration = 0.85f;

        [Header("风味有机味区（仅作用于本体）")]
        [SerializeField, Range(0f, 1f)] private float _flavorVisualIntensity = FlavorOrganicVisual.DefaultIntensity;

        [Header("外轮廓发光（shader alpha outline）")]
        [SerializeField] private Material _outlineGlowMaterial;
        [SerializeField, Range(0f, 0.2f)] private float _placementGlowOutlineWidth = 0.11f;
        [SerializeField] private float _placementGlowInflate = 1.08f;
        [SerializeField, Range(0f, 0.2f)] private float _scopeTargetGlowOutlineWidth = 0.045f;
        [SerializeField] private float _scopeTargetGlowInflate = 1.055f;
        [SerializeField, Range(0.25f, 3f)] private float _scopeTargetGlowIntensity = 1.8f;
        [SerializeField, Range(0.25f, 3f)] private float _outlineGlowIntensity = 2.2f;
        [SerializeField, Range(0f, 8f)] private float _persistentPulseSpeed = 0.65f;
        [SerializeField, Range(0f, 0.5f)] private float _persistentPulseAmplitude = 0.045f;
        [SerializeField, Range(0f, 64f)] private float _pulseFrequency = 18f;

        [Header("结算标签反馈：美味值增加（仅作用于本体视觉枢轴）")]
        [SerializeField] private float _deliciousnessGainPunchScale = 1.18f;
        [SerializeField] private float _deliciousnessGainPunchDuration = 0.18f;
        [SerializeField] private float _deliciousnessGainWobbleDegrees = 5f;
        [SerializeField] private float _deliciousnessGainWobbleDuration = 0.22f;
        [SerializeField] private float _deliciousnessGainWobbleCycles = 2f;

        private Sprite _sprite;
        private float _cellSize;
        private float _pitch;
        private float _liftHeight;
        private Vector3 _shadowBaseLocalPos;
        private Vector3 _shadowBaseScale;
        private float _dragShadowCellScale = 1f;
        private Vector3 _visualBaseLocalPos;
        private Action<DishInstance> _clicked;
        private Action<DishPieceView> _hoverEntered;
        private Action<DishPieceView> _hoverExited;
        private Action<DishPieceView, Vector2> _moveBegin;
        private Action<Vector2> _moveUpdate;
        private Action<Vector2> _moveEnd;
        private Func<DishPieceView, Vector2, bool> _pointerHitFilter;
        private bool _clickEnabled = true;
        private bool _suppressPrimaryUntilReleased;
        private bool _hovered;
        private bool _moveDragging;
        private bool _flying;
        private int _sortingOrderOffset;
        private bool _dragPresentationActive;
        private DishShadowBatchRenderer _shadowBatchRenderer;
        private bool _shadowBatchRegistered;
        private MaterialPropertyBlock _flavorVisualBlock;
        private MaterialPropertyBlock _placementGlowBlock;
        private MaterialPropertyBlock _scopeTargetGlowBlock;
        private readonly Dictionary<BattleScopeHighlightChannel, ScopeTargetGlowState> _scopeTargetGlowStates =
            new Dictionary<BattleScopeHighlightChannel, ScopeTargetGlowState>();
        private Tween _scopeAffectedTween;
        private Transform _scopeAffectedTarget;
        private Vector3 _scopeAffectedBasePosition;
        private int _scopeAffectedVersion;
        private Tween _settlementFeedbackTween;
        private MaterialPropertyBlock _digestBlock;
        private Transform _settlementFeedbackTarget;
        private Vector3 _settlementFeedbackBasePosition;
        private Vector3 _settlementFeedbackBaseScale;
        private Quaternion _settlementFeedbackBaseRotation;
        private int _settlementFeedbackVersion;
        private SettlementDishFeedbackKind _settlementFeedbackKind;
        private bool _sweetTransferSourceActive;
        private bool _sweetTransferExecutorActive;
        private bool _triggerSweetTransferActivatorActive;
        private readonly Dictionary<SpriteRenderer, Color> _activeItemDimColors = new Dictionary<SpriteRenderer, Color>();
        private readonly Dictionary<SpriteRenderer, Color> _settlementFocusColors = new Dictionary<SpriteRenderer, Color>();
        private readonly List<SpriteRenderer> _bodyRenderers = new List<SpriteRenderer>();
        private Tween _settlementFocusTween;
        private float _settlementFocusBrightness = 1f;
        private MaterialPropertyBlock _activeItemTransformBlock;
        private bool _debuffVisualSuppressed;

        internal float PreviewShadowBaseAlpha => _shadowBaseAlpha;
        internal float PreviewShadowGroundScale => _shadowGroundScale;
        internal float PreviewShadowGroundDrop => _shadowGroundDrop;
        internal float PreviewShadowGroundSide => _shadowGroundSide;
        internal float PreviewFlavorVisualIntensity => _flavorVisualIntensity;
        private Sequence _activeItemFlavorSequence;

        private readonly struct ScopeTargetGlowState
        {
            public ScopeTargetGlowState(Color color, int visualIndex)
            {
                Color = color;
                VisualIndex = visualIndex;
            }

            public Color Color { get; }

            public int VisualIndex { get; }
        }

        public DishInstance Instance { get; private set; }

        public int RotationIndex { get; private set; }

        public DishShape CurrentShape { get; private set; }

        internal float CellSize => _cellSize;

        internal float ActiveDragVisualScale =>
            _dragPresentationActive ? Mathf.Max(0.0001f, _dragVisualScale) : 1f;

        internal DishValueBadgeView DishValueBadge =>
            _dishValueBadgePresenter != null ? _dishValueBadgePresenter.View : null;

        internal Vector3 DishValueBadgeWorldPosition =>
            _dishValueBadgePresenter != null
                ? _dishValueBadgePresenter.WorldPosition
                : transform.position;

        internal void ConfigureShadowBatch(DishShadowBatchRenderer renderer)
        {
            if (_shadowBatchRenderer == renderer)
            {
                RefreshShadowBatchRegistration(topologyChanged: true);
                return;
            }

            UnregisterFromShadowBatch();
            _shadowBatchRenderer = renderer;
            RefreshShadowBatchRegistration(topologyChanged: true);
        }

        internal bool TryCaptureShadowSnapshot(Transform batchRoot, out DishShadowSnapshot snapshot)
        {
            if (batchRoot == null
                || _shadowRenderer == null
                || _shadowHaloRenderer == null
                || _shadowRenderer.sprite == null
                || _shadowHaloRenderer.sprite == null)
            {
                snapshot = default;
                return false;
            }

            Matrix4x4 worldToBatch = batchRoot.worldToLocalMatrix;
            snapshot = new DishShadowSnapshot(
                _shadowHaloRenderer.sprite,
                worldToBatch * _shadowHaloRenderer.transform.localToWorldMatrix,
                _shadowHaloRenderer.color,
                _shadowHaloRenderer.flipX,
                _shadowHaloRenderer.flipY,
                _shadowRenderer.sprite,
                worldToBatch * _shadowRenderer.transform.localToWorldMatrix,
                _shadowRenderer.color,
                _shadowRenderer.flipX,
                _shadowRenderer.flipY);
            return true;
        }

        internal void NotifyShadowBatchRegistered(DishShadowBatchRenderer renderer)
        {
            if (_shadowBatchRenderer == renderer)
            {
                _shadowBatchRegistered = true;
            }
        }

        internal void NotifyShadowBatchVisible(DishShadowBatchRenderer renderer)
        {
            if (_shadowBatchRenderer == renderer && _shadowBatchRegistered)
            {
                SetFallbackShadowRendering(false);
            }
        }

        internal void NotifyShadowBatchReleased(DishShadowBatchRenderer renderer)
        {
            if (_shadowBatchRenderer != renderer)
            {
                return;
            }

            _shadowBatchRegistered = false;
            SetFallbackShadowRendering(true);
        }

        internal void AddSweetTransferBuffMarker(SkillActionType actionType)
        {
            // Buff 仍由结算逻辑正常应用，但不再在食物上显示数值标记。
        }

        internal void ClearSweetTransferBuffMarkers()
        {
        }

        public void BuildPlaced(DishInstance instance, Sprite sprite, float cellSize, float pitch, Action<DishInstance> clicked)
        {
            _scopeTargetGlowStates.Clear();
            DishInstance nextInstance = instance ?? throw new ArgumentNullException(nameof(instance));
            DishShape nextShape = nextInstance.Placement.Orientation;
            bool reuseLayout = ReferenceEquals(Instance, nextInstance)
                && CurrentShape == nextShape
                && _sprite == sprite
                && Mathf.Approximately(_cellSize, cellSize)
                && Mathf.Approximately(_pitch, pitch);
            if (!ReferenceEquals(Instance, nextInstance))
            {
                _dishValueBadgePresenter?.Bind(nextInstance);
            }

            Instance = nextInstance;
            _sprite = sprite;
            _cellSize = cellSize;
            _pitch = pitch;
            _clicked = clicked;
            RotationIndex = nextInstance.Placement.RotationIndex;
            CurrentShape = nextShape;
            if (!reuseLayout)
            {
                RebuildCells(CurrentShape);
            }
            else
            {
                // 几何未变时仍要刷新会随战斗状态变化的表现；否则同一实例的
                // 风味、禁用态与分数会一直保留上一帧的材质/文本状态。
                ApplyFlavorVisual();
                ApplyDebuffVisual();
                _dishValueBadgePresenter?.Refresh();
            }
            HideScopeTargetGlow();
        }

        internal void RefreshDishValueBadge()
        {
            if (_dishValueBadgePresenter == null)
            {
                return;
            }

            if (_dishValueBadgePresenter.View == null)
            {
                _dishValueBadgePresenter.UpdateLayout(CurrentShape, _cellSize, _pitch);
            }
            else
            {
                _dishValueBadgePresenter.Refresh();
            }
        }

        internal void SetDishValueBadge(BreakInfinity.BigDouble value)
        {
            _dishValueBadgePresenter?.SetOverride(value);
        }

        internal void ClearDishValueBadgeOverride()
        {
            _dishValueBadgePresenter?.ClearOverride();
        }

        internal void PunchDishValueBadge(float scale, float duration)
        {
            _dishValueBadgePresenter?.Punch(scale, duration);
        }

        internal void SetDishValueBadgeChapterFocused(bool focused, float duration)
        {
            _dishValueBadgePresenter?.SetChapterFocused(focused, duration);
        }

        internal void SetDishValueBadgeVisible(bool visible)
        {
            _dishValueBadgePresenter?.SetVisible(visible);
        }

        internal void FadeDishValueBadge(bool visible, float duration, Action onComplete = null)
        {
            if (_dishValueBadgePresenter == null)
            {
                onComplete?.Invoke();
                return;
            }

            _dishValueBadgePresenter.Fade(visible, duration, onComplete);
        }

        public void UpdatePlacement(Placement placement)
        {
            if (Instance == null)
            {
                return;
            }

            Instance.Relocate(placement);
            RotationIndex = placement.RotationIndex;
            CurrentShape = placement.Orientation;
            RebuildCells(CurrentShape);
        }

        /// <summary>是否响应普通点击（打开详情）。目标选择等互斥交互期间可临时关闭。</summary>
        public void SetClickEnabled(bool enabled)
        {
            bool wasEnabled = _clickEnabled;
            _clickEnabled = enabled;
            if (!enabled)
            {
                _suppressPrimaryUntilReleased = false;
                SetHovered(false);
                return;
            }

            // 消耗品会在目标点击成功的同一帧恢复食物交互。
            // 等这次左键完全释放后再接收点击，避免同一次按下穿透成食物拖拽。
            if (!wasEnabled && (WorldInput.PrimaryHeld || WorldInput.PrimaryPressedThisFrame))
            {
                _suppressPrimaryUntilReleased = true;
            }
        }

        public void SetHoverCallbacks(Action<DishPieceView> entered, Action<DishPieceView> exited)
        {
            _hoverEntered = entered;
            _hoverExited = exited;
        }

        /// <summary>
        /// 为重叠表现注入唯一命中过滤器。Hover、点击和拖拽起始共用同一判定，
        /// 避免多个 DishPieceView 在同一帧分别抢占输入。
        /// </summary>
        public void SetPointerHitFilter(Func<DishPieceView, Vector2, bool> filter)
        {
            _pointerHitFilter = filter;
            SetHovered(false);
        }

        /// <summary>
        /// 仅给“本次刚上桌”的菜注入移动回调。其它已锁定菜保持 null，因此完全不可拖拽。
        /// </summary>
        public void SetMoveCallbacks(
            Action<DishPieceView, Vector2> begin,
            Action<Vector2> update,
            Action<Vector2> end)
        {
            _moveBegin = begin;
            _moveUpdate = update;
            _moveEnd = end;
            if (begin == null)
            {
                _moveDragging = false;
            }
        }

        public bool ContainsWorldPoint(Vector2 world)
        {
            return ContainsOccupiedCellAtWorldPoint(world);
        }

        public Bounds WorldBounds
        {
            get
            {
                EnsureRefs();
                if (_spriteRenderer != null && _spriteRenderer.sprite != null)
                {
                    return _spriteRenderer.bounds;
                }

                if (_collider != null)
                {
                    return _collider.bounds;
                }

                return new Bounds(transform.position, Vector3.one);
            }
        }

        public bool TryCaptureGrabVisual(Camera camera, out DishGrabVisualSnapshot snapshot)
        {
            snapshot = default;
            EnsureRefs();
            if (camera == null || _spriteRenderer == null || _spriteRenderer.sprite == null)
            {
                return false;
            }

            Bounds spriteBounds = _spriteRenderer.sprite.bounds;
            Transform rendererTransform = _spriteRenderer.transform;
            Vector3 localCenter = spriteBounds.center;
            Vector2 center = camera.WorldToScreenPoint(rendererTransform.TransformPoint(localCenter));
            Vector2 right = camera.WorldToScreenPoint(rendererTransform.TransformPoint(
                localCenter + Vector3.right * spriteBounds.extents.x));
            Vector2 up = camera.WorldToScreenPoint(rendererTransform.TransformPoint(
                localCenter + Vector3.up * spriteBounds.extents.y));
            Vector2 rightDelta = right - center;
            Vector2 upDelta = up - center;
            Vector2 size = new Vector2(rightDelta.magnitude * 2f, upDelta.magnitude * 2f);
            if (size.x <= 0.5f || size.y <= 0.5f)
            {
                return false;
            }

            snapshot = new DishGrabVisualSnapshot(
                _spriteRenderer.sprite,
                _spriteRenderer.color,
                center,
                size,
                Mathf.Atan2(rightDelta.y, rightDelta.x) * Mathf.Rad2Deg,
                _spriteRenderer.flipX,
                _spriteRenderer.flipY,
                Instance != null
                    ? new List<string>(Instance.FlavorIds)
                    : Array.Empty<string>(),
                Instance != null ? Instance.Id : 0f,
                _flavorVisualIntensity);
            return true;
        }

        public void SetDebuffVisualSuppressed(bool suppressed)
        {
            _debuffVisualSuppressed = suppressed;
            if (_spriteRenderer == null)
            {
                return;
            }

            if (suppressed)
            {
                DebuffVisualStyle.ClearSprite(_spriteRenderer);
                ApplyFlavorVisual();
            }
            else
            {
                ApplyDebuffVisual();
            }
        }

        public void RevealDebuffVisual() => SetDebuffVisualSuppressed(false);

        /// <summary>
        /// 放置可否的外轮廓发光：绿=可放，红=不可放；关闭则隐藏。
        /// 用 shader 按 sprite alpha 边界采样外描边，避免出现整张 sprite 复制投影。
        /// </summary>
        public void SetPlacementGlow(bool show, bool valid)
        {
            EnsureRefs();
            if (!show)
            {
                HidePlacementGlow();
                return;
            }

            if (_spriteRenderer == null)
            {
                return;
            }

            if (_placementGlow == null)
            {
                Debug.LogError($"{nameof(DishPieceView)} prefab 缺少 PlacementGlow。", this);
                return;
            }

            _placementGlow.gameObject.SetActive(true);
            Color color = valid
                ? new Color(0.30f, 1f, 0.42f, 0.9f)
                : new Color(1f, 0.32f, 0.30f, 0.9f);
            ConfigureOutlineGlowRenderer(
                _placementGlow,
                ref _placementGlowBlock,
                color,
                _placementGlowOutlineWidth,
                fillAlpha: 0f,
                inflate: _placementGlowInflate,
                sortingOrderOffset: 1,
                materialOverride: null,
                pulseSpeed: _persistentPulseSpeed,
                pulseAmplitude: _persistentPulseAmplitude);
        }

        /// <summary>
        /// 设置某个 scope 展示通道的实际目标发光。同一通道只保留最高 VisualIndex，
        /// 不同通道按 Settlement &gt; Flash &gt; Persistent 仲裁。
        /// </summary>
        public void SetScopeTargetGlow(
            BattleScopeHighlightChannel channel,
            Color color,
            int visualIndex)
        {
            EnsureRefs();
            if (_scopeTargetGlow == null)
            {
                return;
            }

            if (_scopeTargetGlowStates.TryGetValue(channel, out ScopeTargetGlowState current)
                && current.VisualIndex > visualIndex)
            {
                return;
            }

            _scopeTargetGlowStates[channel] = new ScopeTargetGlowState(color, visualIndex);
            RefreshScopeTargetGlow();
        }

        public void ClearScopeTargetGlow(BattleScopeHighlightChannel channel)
        {
            if (_scopeTargetGlowStates.Remove(channel))
            {
                RefreshScopeTargetGlow();
            }
        }

        public void ClearAllScopeTargetGlows()
        {
            _scopeTargetGlowStates.Clear();
            HideScopeTargetGlow();
        }

        internal BattleScopeHighlightChannel? ActiveScopeTargetGlowChannel { get; private set; }

        private void RefreshScopeTargetGlow()
        {
            if (_scopeTargetGlow == null || _scopeTargetGlowStates.Count == 0)
            {
                HideScopeTargetGlow();
                return;
            }

            bool found = false;
            BattleScopeHighlightChannel activeChannel = BattleScopeHighlightChannel.Persistent;
            ScopeTargetGlowState activeState = default;
            foreach (KeyValuePair<BattleScopeHighlightChannel, ScopeTargetGlowState> entry in _scopeTargetGlowStates)
            {
                if (!found || (int)entry.Key > (int)activeChannel)
                {
                    found = true;
                    activeChannel = entry.Key;
                    activeState = entry.Value;
                }
            }

            if (!found)
            {
                HideScopeTargetGlow();
                return;
            }

            float pulseSpeed = activeChannel switch
            {
                BattleScopeHighlightChannel.Flash => Mathf.Max(1.8f, _persistentPulseSpeed),
                BattleScopeHighlightChannel.Settlement => Mathf.Max(1.2f, _persistentPulseSpeed),
                _ => _persistentPulseSpeed,
            };
            float pulseAmplitude = activeChannel switch
            {
                BattleScopeHighlightChannel.Flash => Mathf.Max(0.1f, _persistentPulseAmplitude),
                BattleScopeHighlightChannel.Settlement => Mathf.Max(0.08f, _persistentPulseAmplitude),
                _ => _persistentPulseAmplitude,
            };

            ActiveScopeTargetGlowChannel = activeChannel;
            _scopeTargetGlow.gameObject.SetActive(true);
            ConfigureOutlineGlowRenderer(
                _scopeTargetGlow,
                ref _scopeTargetGlowBlock,
                activeState.Color,
                _scopeTargetGlowOutlineWidth,
                fillAlpha: 0f,
                inflate: _scopeTargetGlowInflate,
                sortingOrderOffset: 3,
                materialOverride: null,
                pulseSpeed,
                pulseAmplitude,
                innerAlpha: 0f,
                glowIntensity: _scopeTargetGlowIntensity);
        }

        private void HideScopeTargetGlow()
        {
            ActiveScopeTargetGlowChannel = null;
            if (_scopeTargetGlow == null)
            {
                return;
            }

            _scopeTargetGlowBlock ??= new MaterialPropertyBlock();
            _scopeTargetGlowBlock.Clear();
            _scopeTargetGlow.SetPropertyBlock(_scopeTargetGlowBlock);
            _scopeTargetGlow.gameObject.SetActive(false);
        }

        public void SetGhost(bool ghost)
        {
            EnsureRefs();
            if (_spriteRenderer == null)
            {
                return;
            }

            // 只调食物本体透明度；独立 Glow 层由各自通道管理，不能随本体一起改色。
            foreach (SpriteRenderer renderer in EnumerateBodyRenderers())
            {
                Color color = renderer.color;
                color.a = ghost ? Mathf.Min(color.a, 0.65f) : Mathf.Max(color.a, 0.95f);
                renderer.color = color;
            }
        }

        /// <summary>
        /// 强化餐桌选格期间，把食物本体降至原透明度的 50%，并精确恢复每个渲染体原色。
        /// 与拖拽 Ghost 分开管理，避免退出目标选择后把原始 alpha 粗暴改成固定值。
        /// </summary>
        public void SetActiveItemTargetDimmed(bool dimmed)
        {
            EnsureRefs();
            _dishValueBadgePresenter?.SetDimmed(dimmed);
            if (_spriteRenderer == null)
            {
                return;
            }

            if (!dimmed)
            {
                foreach (KeyValuePair<SpriteRenderer, Color> entry in _activeItemDimColors)
                {
                    if (entry.Key != null)
                    {
                        entry.Key.color = entry.Value;
                    }
                }

                _activeItemDimColors.Clear();
                return;
            }

            if (_activeItemDimColors.Count > 0)
            {
                return;
            }

            foreach (SpriteRenderer renderer in EnumerateBodyRenderers())
            {
                Color original = renderer.color;
                _activeItemDimColors[renderer] = original;
                original.a *= 0.5f;
                renderer.color = original;
            }
        }

        /// <summary>
        /// 结算舞台独立亮度通道。首次调用时精确记录当前颜色，后续亮度变化都从记录值计算，
        /// 不覆盖消耗品选择或 Ghost 状态。
        /// </summary>
        public void SetSettlementFocus(float brightness)
        {
            SetSettlementFocus(brightness, 0f);
        }

        public void SetSettlementFocus(float brightness, float duration)
        {
            EnsureRefs();
            if (_spriteRenderer == null)
            {
                return;
            }

            if (_settlementFocusColors.Count == 0)
            {
                foreach (SpriteRenderer renderer in EnumerateBodyRenderers())
                {
                    _settlementFocusColors[renderer] = renderer.color;
                }

                _settlementFocusBrightness = 1f;
            }

            float targetBrightness = Mathf.Clamp01(brightness);
            _settlementFocusTween?.Kill();
            _settlementFocusTween = null;
            if (duration <= 0.0001f)
            {
                ApplySettlementFocusBrightness(targetBrightness);
                return;
            }

            _settlementFocusTween = DOVirtual.Float(
                    _settlementFocusBrightness,
                    targetBrightness,
                    duration,
                    ApplySettlementFocusBrightness)
                .SetEase(Ease.OutQuad)
                .SetLink(gameObject)
                .OnComplete(() => _settlementFocusTween = null);
        }

        private void ApplySettlementFocusBrightness(float brightness)
        {
            _settlementFocusBrightness = Mathf.Clamp01(brightness);
            foreach (KeyValuePair<SpriteRenderer, Color> entry in _settlementFocusColors)
            {
                if (entry.Key == null)
                {
                    continue;
                }

                Color color = entry.Value;
                color.r *= _settlementFocusBrightness;
                color.g *= _settlementFocusBrightness;
                color.b *= _settlementFocusBrightness;
                entry.Key.color = color;
            }
        }

        public void ClearSettlementFocus()
        {
            _settlementFocusTween?.Kill();
            _settlementFocusTween = null;
            foreach (KeyValuePair<SpriteRenderer, Color> entry in _settlementFocusColors)
            {
                if (entry.Key != null)
                {
                    entry.Key.color = entry.Value;
                }
            }

            _settlementFocusColors.Clear();
            _settlementFocusBrightness = 1f;
        }

        public void SetBodyRenderersEnabled(bool enabled)
        {
            EnsureRefs();
            foreach (SpriteRenderer renderer in EnumerateBodyRenderers())
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        /// <summary>一次性表现使用：统一调整食物本体透明度，让 Overlay 拖拽残影同步淡出。</summary>
        internal void SetBodyAlpha(float alpha)
        {
            EnsureRefs();
            float clamped = Mathf.Clamp01(alpha);
            foreach (SpriteRenderer renderer in EnumerateBodyRenderers())
            {
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = clamped;
                renderer.color = color;
            }
        }

        private List<SpriteRenderer> EnumerateBodyRenderers()
        {
            if (_bodyRenderers.Count > 0 || _spriteRenderer == null)
            {
                return _bodyRenderers;
            }

            _spriteRenderer.GetComponentsInChildren(true, _bodyRenderers);
            for (int i = _bodyRenderers.Count - 1; i >= 0; i--)
            {
                SpriteRenderer renderer = _bodyRenderers[i];
                if (renderer == null || renderer == _placementGlow || renderer == _scopeTargetGlow)
                {
                    _bodyRenderers.RemoveAt(i);
                }
            }

            return _bodyRenderers;
        }

        /// <summary>
        /// 主动调味的纯表现动画。业务数据已在调用前写入；中点只刷新风味污渍。
        /// Transform Shader 缺失时回退为缩放 Punch。
        /// </summary>
        public void PlayActiveItemFlavorTransform(Action onVisualSwitch, Action onComplete)
        {
            EnsureRefs();
            _activeItemFlavorSequence?.Kill();
            _activeItemFlavorSequence = null;
            Transform target = _visualPivot != null ? _visualPivot : transform;

            if (_spriteRenderer == null || SpriteRenderStyle.SpriteTransformMaterial == null)
            {
                _activeItemFlavorSequence = DOTween.Sequence()
                    .Append(target.DOPunchScale(Vector3.one * 0.12f, 0.28f, vibrato: 7, elasticity: 0.65f))
                    .InsertCallback(0.14f, () =>
                    {
                        onVisualSwitch?.Invoke();
                        ApplyFlavorVisual();
                    })
                    .OnComplete(() =>
                    {
                        _activeItemFlavorSequence = null;
                        onComplete?.Invoke();
                    });
                return;
            }

            SpriteRenderStyle.ApplyTransformMaterial(_spriteRenderer);
            ApplyActiveItemTransformEffect(0f);
            _activeItemFlavorSequence = DOTween.Sequence()
                .Append(DOTween.To(() => 0f, ApplyActiveItemTransformEffect, 1f, 0.14f).SetEase(Ease.OutQuad))
                .AppendCallback(() =>
                {
                    onVisualSwitch?.Invoke();
                    ApplyFlavorVisual();
                    ApplyActiveItemTransformEffect(1f);
                })
                .Append(DOTween.To(() => 1f, ApplyActiveItemTransformEffect, 0f, 0.2f).SetEase(Ease.InOutQuad))
                .OnComplete(() =>
                {
                    _activeItemFlavorSequence = null;
                    ApplyFlavorVisual();
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// 麻风味专用变化：先把当前食物按新摆放朝向做可见的逆时针旋转，再切换真实占格表现。
        /// 数据层在调用前已经完成迁移；这里只负责从旧朝向平滑过渡到新朝向。
        /// </summary>
        public void PlayActiveItemNumbTransform(Placement rotatedPlacement, Action onComplete)
        {
            EnsureRefs();
            _activeItemFlavorSequence?.Kill();
            _activeItemFlavorSequence = null;

            Transform target = _visualPivot != null ? _visualPivot : transform;
            Vector3 lockedVisualCenterWorld = target.position;
            int ccwSteps = ((RotationIndex - rotatedPlacement.RotationIndex) % 4 + 4) % 4;
            float angle = ccwSteps * 90f;
            Quaternion baseRotation = target.localRotation;

            if (_spriteRenderer != null && SpriteRenderStyle.SpriteTransformMaterial != null)
            {
                SpriteRenderStyle.ApplyTransformMaterial(_spriteRenderer);
                ApplyActiveItemTransformEffect(0f);
            }

            _activeItemFlavorSequence = DOTween.Sequence()
                .Append(target.DOLocalRotate(
                        new Vector3(0f, 0f, angle),
                        0.28f,
                        RotateMode.LocalAxisAdd)
                    .SetEase(Ease.OutBack))
                .Join(DOTween.To(
                        () => 0f,
                        ApplyActiveItemTransformEffect,
                        1f,
                        0.14f)
                    .SetEase(Ease.OutQuad))
                .AppendCallback(() =>
                {
                    target.localRotation = baseRotation;
                    UpdatePlacement(rotatedPlacement);
                    // 新朝向会重建 VisualPivot 的局部位置；补偿根节点以锁住同一个世界视觉中心。
                    transform.position += lockedVisualCenterWorld - target.position;
                    ApplyFlavorVisual();
                    ApplyActiveItemTransformEffect(1f);
                })
                .Append(DOTween.To(
                        () => 1f,
                        ApplyActiveItemTransformEffect,
                        0f,
                        0.18f)
                    .SetEase(Ease.InOutQuad))
                .OnComplete(() =>
                {
                    _activeItemFlavorSequence = null;
                    ApplyActiveItemTransformEffect(0f);
                    ApplyFlavorVisual();
                    onComplete?.Invoke();
                });
        }

        private void ApplyActiveItemTransformEffect(float amount)
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            float t = Mathf.Clamp01(amount);
            _activeItemTransformBlock ??= new MaterialPropertyBlock();
            _spriteRenderer.GetPropertyBlock(_activeItemTransformBlock);
            _activeItemTransformBlock.SetFloat(BrightnessId, t);
            _activeItemTransformBlock.SetVector(BoingId, new Vector4(0.16f * t, -0.12f * t, 0f, 0f));
            _activeItemTransformBlock.SetVector(EdgeClampPointId, new Vector4(0.24f, 0.24f, 0f, 0f));
            _spriteRenderer.SetPropertyBlock(_activeItemTransformBlock);
        }

        /// <summary>
        /// 悬浮/飞行时只把本体切到 PiecesFlying；两层假阴影始终留在地面的 Pieces，
        /// 被沿途已摆放食物遮挡，避免飞行层阴影在棋盘上形成脏暗斑。
        /// </summary>
        public void SetFlying(bool flying)
        {
            EnsureRefs();
            _flying = flying;
            string bodyLayer = flying ? BattleSorting.PiecesFlying : BattleSorting.Pieces;
            if (_spriteRenderer != null)
            {
                foreach (SpriteRenderer renderer in _spriteRenderer.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    renderer.sortingLayerName = bodyLayer;
                }

                _spriteRenderer.sortingOrder = BattleSorting.OrderBody + _sortingOrderOffset;
            }

            if (_shadowRenderer != null)
            {
                BattleSorting.Apply(
                    _shadowRenderer,
                    BattleSorting.Pieces,
                    BattleSorting.OrderShadow + _sortingOrderOffset);
            }

            if (_shadowHaloRenderer != null)
            {
                BattleSorting.Apply(
                    _shadowHaloRenderer,
                    BattleSorting.Pieces,
                    BattleSorting.OrderShadow - 1 + _sortingOrderOffset);
            }

            if (_placementGlow != null)
            {
                _placementGlow.sortingLayerName = bodyLayer;
                _placementGlow.sortingOrder = _spriteRenderer != null
                    ? _spriteRenderer.sortingOrder + 1
                    : BattleSorting.OrderBody + 1 + _sortingOrderOffset;
            }

            if (_scopeTargetGlow != null)
            {
                _scopeTargetGlow.sortingLayerName = bodyLayer;
                _scopeTargetGlow.sortingOrder = _spriteRenderer != null
                    ? _spriteRenderer.sortingOrder + 3
                    : BattleSorting.OrderBody + 3 + _sortingOrderOffset;
            }

            _dishValueBadgePresenter?.SetSorting(_flying, _sortingOrderOffset);
            RefreshShadowBatchRegistration(topologyChanged: false);
        }

        /// <summary>
        /// 临时桌横向叠放时，以整份菜为单位调整层内顺序。
        /// 偏移同时作用于本体、阴影和描边，避免新菜只盖住旧菜的一部分表现层。
        /// </summary>
        public void SetSortingOrderOffset(int offset)
        {
            _sortingOrderOffset = offset;
            SetFlying(_flying);
        }

        /// <summary>
        /// 切换统一拖拽悬浮表现。本体进入 PiecesFlying 并保持不透明放大；
        /// 两层轮廓影留在地面的 Pieces，只做低透明度的轻微外扩与偏移。
        /// </summary>
        public void SetDragPresentation(bool active, float targetCellSize = 0f)
        {
            EnsureRefs();
            _scopeAffectedVersion++;
            StopScopeAffectedShake(restoreTransform: true);
            _dragPresentationActive = active;
            _dragShadowCellScale = active && targetCellSize > 0f
                ? targetCellSize / Mathf.Max(_cellSize, 0.0001f)
                : 1f;
            _liftHeight = 0f;
            SetFlying(active);

            if (_placementGlow != null)
            {
                _placementGlow.gameObject.SetActive(false);
            }

            if (_spriteRenderer != null)
            {
                Color body = _spriteRenderer.color;
                body.a = 1f;
                _spriteRenderer.color = body;
            }

            if (active)
            {
                ApplyDragPresentation();
                SetDishValueBadgeVisible(false);
                return;
            }

            SetDishValueBadgeVisible(true);
            SetBodyRenderersEnabled(true);
            Transform target = VisualAnimationTarget();
            if (target != null)
            {
                target.localScale = Vector3.one;
                target.localRotation = Quaternion.identity;
            }

            ApplyLiftHeight(0f);
        }

        /// <summary>把实际渲染出来的食物本体中心移动到指定世界点；合法位置也不会在拖拽中吸格。</summary>
        public void MoveVisualCenterToWorld(Vector3 centerWorld)
        {
            EnsureRefs();
            Vector3 currentCenter = _spriteRenderer != null
                ? _spriteRenderer.bounds.center
                : OccupiedCellCenterWorld();
            transform.position += centerWorld - currentCenter;
            _shadowBatchRenderer?.MarkStreamDirty(this);
        }

        public Vector2 FootprintWorldSize
        {
            get
            {
                if (CurrentShape == null)
                {
                    return Vector2.one * Mathf.Max(_cellSize, 0.01f);
                }

                return new Vector2(
                    Mathf.Max(_cellSize, (CurrentShape.Width - 1) * _pitch + _cellSize),
                    Mathf.Max(_cellSize, (CurrentShape.Height - 1) * _pitch + _cellSize));
            }
        }

        /// <summary>只缩放食物本体视觉枢轴，不改变根节点格子锚点、阴影计算和碰撞盒。</summary>
        public void SetVisualScaleMultiplier(float scale)
        {
            EnsureRefs();
            Transform target = VisualAnimationTarget();
            if (target != null)
            {
                float safeScale = Mathf.Max(0.0001f, scale);
                target.localScale = new Vector3(safeScale, safeScale, 1f);
            }
        }

        /// <summary>一次性飞行动画使用：绕食物视觉中心旋转，不改动根节点和落格坐标。</summary>
        internal void SetVisualRotationDegrees(float degrees)
        {
            EnsureRefs();
            Transform target = VisualAnimationTarget();
            if (target != null)
            {
                target.localRotation = Quaternion.Euler(0f, 0f, degrees);
            }
        }

        /// <summary>
        /// 当前朝向下，占用格中心点的平均位置（相对食物根节点）。
        /// 拖拽时用它把不规则形状的视觉重心对准鼠标，而不是把原点格对准鼠标。
        /// </summary>
        public Vector3 OccupiedCellCenterLocal()
        {
            return CurrentShape != null ? VisualPivotLocal(CurrentShape) : Vector3.zero;
        }

        /// <summary>当前占格视觉重心的世界坐标。</summary>
        public Vector3 OccupiedCellCenterWorld()
        {
            return transform.TransformPoint(OccupiedCellCenterLocal());
        }

        /// <summary>指定视觉缩放下，食品实际渲染中心相对根节点（原点格锚点）的偏移。</summary>
        public Vector3 VisualCenterOffsetForScale(float scale)
        {
            EnsureRefs();

            Transform target = VisualAnimationTarget();
            if (target == null || _spriteRenderer == null || _spriteRenderer.sprite == null)
            {
                return CurrentShape != null ? FootprintCenterLocal(CurrentShape) : Vector3.zero;
            }

            float safeScale = Mathf.Max(0.0001f, scale);
            Vector3 savedScale = target.localScale;
            Vector3 savedPos = target.localPosition;

            // 量"地面态"视觉中心：临时清掉抬升偏移，避免把当前飞行高度算进对齐基准（否则计算会自指）。
            bool restorePos = target == _visualPivot;
            if (restorePos)
            {
                target.localPosition = _visualBaseLocalPos;
            }

            target.localScale = new Vector3(safeScale, safeScale, 1f);
            Vector3 offset = transform.InverseTransformPoint(_spriteRenderer.bounds.center);
            target.localScale = savedScale;
            if (restorePos)
            {
                target.localPosition = savedPos;
            }

            return offset;
        }

        public Awaitable PlayLandFeedbackAsync(CancellationToken cancellationToken)
        {
            EnsureRefs();
            return PresentationTween.PunchScaleAndWobbleAsync(
                VisualAnimationTarget(),
                _landPunchScale,
                _landPunchDuration,
                _landWobbleDegrees,
                _landWobbleCycles,
                _landWobbleDuration,
                cancellationToken);
        }

        /// <summary>
        /// 放置后提示“会被新食物 scope 影响”：围绕当前位置做很轻的衰减抖动，
        /// 不移动食物根节点，因此不会改变占格、碰撞盒或餐桌数据。
        /// </summary>
        public void PlayScopeAffectedShake(float durationScale = 1f)
        {
            EnsureRefs();
            int version = ++_scopeAffectedVersion;
            StopScopeAffectedShake(restoreTransform: true);

            Transform target = VisualAnimationTarget();
            if (target == null)
            {
                return;
            }

            _scopeAffectedTarget = target;
            _scopeAffectedBasePosition = target.localPosition;
            float duration = Mathf.Max(
                0.0001f,
                _scopeAffectedShakeDuration * Mathf.Max(0.05f, durationScale));
            float cycles = Mathf.Max(0f, _scopeAffectedShakeCycles);
            float distance = Mathf.Max(0f, _scopeAffectedShakeCells) * Mathf.Max(_cellSize, 0.01f);
            Vector3 basePosition = _scopeAffectedBasePosition;

            _scopeAffectedTween = DOVirtual.Float(0f, 1f, duration, progress =>
                {
                    if (target == null || version != _scopeAffectedVersion)
                    {
                        return;
                    }

                    float decay = 1f - progress;
                    float phase = progress * cycles * Mathf.PI * 2f;
                    float x = Mathf.Sin(phase) * distance * decay;
                    float y = Mathf.Sin(phase * 2f + Mathf.PI * 0.35f) * distance * 0.18f * decay;
                    target.localPosition = basePosition + new Vector3(x, y, 0f);
                })
                .SetEase(Ease.Linear)
                .SetLink(target.gameObject)
                .OnComplete(() =>
                {
                    if (target != null && version == _scopeAffectedVersion)
                    {
                        target.localPosition = basePosition;
                        _scopeAffectedTween = null;
                        _scopeAffectedTarget = null;
                    }
                });
        }

        public Awaitable PlayServeLandImpactFeedbackAsync(CancellationToken cancellationToken)
        {
            EnsureRefs();
            return PresentationTween.PunchLocalScaleAsync(
                VisualAnimationTarget(),
                _serveLandImpactScale,
                _serveLandImpactDuration,
                cancellationToken);
        }

        /// <summary>
        /// 播放“被消化”溶解：以实际占格的平均位置为视觉中心，从不规则食物的外圈向内收缩。
        /// 溶解只作用于本体，脚下阴影同步淡出；结束后由餐桌重建销毁该临时表现。
        /// </summary>
        public async Awaitable PlayDigestDissolveAsync(CancellationToken cancellationToken)
        {
            EnsureRefs();
            if (_spriteRenderer == null)
            {
                return;
            }

            _clickEnabled = false;
            SetHovered(false);
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            if (_placementGlow != null)
            {
                _placementGlow.gameObject.SetActive(false);
            }

            Material dissolveMaterial = SpriteRenderStyle.DigestDissolveMaterial;
            if (dissolveMaterial == null)
            {
                await FadeDigestFallbackAsync(cancellationToken);
                return;
            }

            _digestBlock ??= new MaterialPropertyBlock();
            _spriteRenderer.GetPropertyBlock(_digestBlock);
            _digestBlock.SetVector(SpriteUvRectId, SpriteUvRect(_spriteRenderer.sprite));
            _digestBlock.SetVector(DigestCenterId, DigestCenterInSpriteRect());
            _digestBlock.SetVector(DigestGridSizeId, DigestGridSize());
            _digestBlock.SetFloat(DigestSeedId, Instance != null ? Instance.Id * 1.6180339f : 0f);
            _digestBlock.SetFloat(DigestProgressId, 0f);
            SpriteRenderStyle.ApplyDigestDissolveMaterial(_spriteRenderer);
            _spriteRenderer.SetPropertyBlock(_digestBlock);

            float shadowAlpha = _shadowRenderer != null ? _shadowRenderer.color.a : 0f;
            float haloAlpha = _shadowHaloRenderer != null ? _shadowHaloRenderer.color.a : 0f;
            float duration = Mathf.Max(0.0001f, _digestDissolveDuration);
            Tween tween = DOVirtual.Float(0f, 1f, duration, progress =>
                {
                    if (_spriteRenderer == null)
                    {
                        return;
                    }

                    float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
                    _digestBlock.SetFloat(DigestProgressId, eased);
                    _spriteRenderer.SetPropertyBlock(_digestBlock);
                    FadeDigestShadow(eased, shadowAlpha, haloAlpha);
                })
                .SetEase(Ease.Linear)
                .SetLink(gameObject);

            await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
            if (_spriteRenderer != null)
            {
                _digestBlock.SetFloat(DigestProgressId, 1f);
                _spriteRenderer.SetPropertyBlock(_digestBlock);
            }

            FadeDigestShadow(1f, shadowAlpha, haloAlpha);
        }

        private async Awaitable FadeDigestFallbackAsync(CancellationToken cancellationToken)
        {
            Color body = _spriteRenderer.color;
            float shadowAlpha = _shadowRenderer != null ? _shadowRenderer.color.a : 0f;
            float haloAlpha = _shadowHaloRenderer != null ? _shadowHaloRenderer.color.a : 0f;
            Tween tween = DOVirtual.Float(0f, 1f, Mathf.Max(0.0001f, _digestDissolveDuration), progress =>
                {
                    float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
                    if (_spriteRenderer != null)
                    {
                        Color faded = body;
                        faded.a = body.a * (1f - eased);
                        _spriteRenderer.color = faded;
                    }

                    FadeDigestShadow(eased, shadowAlpha, haloAlpha);
                })
                .SetEase(Ease.Linear)
                .SetLink(gameObject);
            await PresentationTween.AwaitCompletionAsync(tween, cancellationToken);
        }

        private Vector2 DigestCenterInSpriteRect()
        {
            if (_sprite == null || CurrentShape == null)
            {
                return new Vector2(0.5f, 0.5f);
            }

            Vector3 visualCenterWorld = transform.TransformPoint(VisualPivotLocal(CurrentShape));
            Vector3 spriteLocal = _spriteRenderer.transform.InverseTransformPoint(visualCenterWorld);
            Bounds bounds = _sprite.bounds;
            return new Vector2(
                bounds.size.x > 0.0001f ? Mathf.Clamp01((spriteLocal.x - bounds.min.x) / bounds.size.x) : 0.5f,
                bounds.size.y > 0.0001f ? Mathf.Clamp01((spriteLocal.y - bounds.min.y) / bounds.size.y) : 0.5f);
        }

        private Vector2 DigestGridSize()
        {
            if (CurrentShape == null)
            {
                return Vector2.one;
            }

            int rot = ((RotationIndex % 4) + 4) % 4;
            return (rot & 1) == 0
                ? new Vector2(Mathf.Max(1, CurrentShape.Width), Mathf.Max(1, CurrentShape.Height))
                : new Vector2(Mathf.Max(1, CurrentShape.Height), Mathf.Max(1, CurrentShape.Width));
        }

        private void FadeDigestShadow(float progress, float shadowAlpha, float haloAlpha)
        {
            float remaining = 1f - Mathf.Clamp01(progress);
            if (_shadowRenderer != null)
            {
                Color color = _shadowRenderer.color;
                color.a = shadowAlpha * remaining;
                _shadowRenderer.color = color;
            }

            if (_shadowHaloRenderer != null)
            {
                Color color = _shadowHaloRenderer.color;
                color.a = haloAlpha * remaining;
                _shadowHaloRenderer.color = color;
            }

            _shadowBatchRenderer?.MarkStreamDirty(this);
        }

        public Awaitable PlayDeliciousnessGainFeedbackAsync(CancellationToken cancellationToken)
        {
            return PlaySettlementFeedbackAsync(SettlementDishFeedbackKind.GenericValueChanged, cancellationToken);
        }

        public void BeginSweetTransferSourceFeedback()
        {
            EnsureRefs();
            _sweetTransferSourceActive = true;
            if (_settlementFeedbackTween == null || !_settlementFeedbackTween.active)
            {
                HidePlacementGlow();
            }
        }

        public void EndSweetTransferSourceFeedback()
        {
            bool stopSourceIntro = _settlementFeedbackKind == SettlementDishFeedbackKind.SweetTransferSkillTriggered;
            _sweetTransferSourceActive = false;
            if (stopSourceIntro)
            {
                _settlementFeedbackVersion++;
                StopSettlementFeedback(restoreTransform: true);
                return;
            }

            if (_settlementFeedbackTween == null || !_settlementFeedbackTween.active)
            {
                RestorePersistentSweetTransferGlow();
            }
        }

        public void BeginSweetTransferExecutorFeedback()
        {
            EnsureRefs();
            _sweetTransferExecutorActive = true;
            if (_settlementFeedbackTween == null || !_settlementFeedbackTween.active)
            {
                HidePlacementGlow();
            }
        }

        public void EndSweetTransferExecutorFeedback()
        {
            _sweetTransferExecutorActive = false;
            if (_settlementFeedbackTween == null || !_settlementFeedbackTween.active)
            {
                RestorePersistentSweetTransferGlow();
            }
        }

        /// <summary>进入「代触发甜蜜传递」持续状态：施放者在所有来源食物执行期间保持金色脉冲外发光。</summary>
        public void BeginTriggerSweetTransferActivatorFeedback()
        {
            EnsureRefs();
            _triggerSweetTransferActivatorActive = true;
            if (_settlementFeedbackTween == null || !_settlementFeedbackTween.active)
            {
                ShowTriggerSweetTransferActivatorGlow();
            }
        }

        public void EndTriggerSweetTransferActivatorFeedback()
        {
            _triggerSweetTransferActivatorActive = false;
            if ((_settlementFeedbackTween == null || !_settlementFeedbackTween.active) && _placementGlow != null)
            {
                RestorePersistentSweetTransferGlow();
            }
        }

        public async Awaitable PlaySettlementFeedbackAsync(
            SettlementDishFeedbackKind kind,
            CancellationToken cancellationToken,
            float durationScale = 1f)
        {
            EnsureRefs();
            _scopeAffectedVersion++;
            StopScopeAffectedShake(restoreTransform: true);
            int version = ++_settlementFeedbackVersion;
            StopSettlementFeedback(restoreTransform: true);

            if (kind == SettlementDishFeedbackKind.None)
            {
                return;
            }

            Transform target = VisualAnimationTarget();
            if (target == null)
            {
                return;
            }

            SettlementFeedbackProfile profile = BuildSettlementFeedbackProfile(kind);
            _settlementFeedbackTarget = target;
            _settlementFeedbackBasePosition = target.localPosition;
            _settlementFeedbackBaseScale = target.localScale;
            _settlementFeedbackBaseRotation = target.localRotation;
            _settlementFeedbackKind = kind;
            float safeDurationScale = Mathf.Max(0.05f, durationScale);
            ShowSettlementGlow(profile, safeDurationScale);

            float duration = Mathf.Max(0.0001f, profile.Duration * safeDurationScale);
            _settlementFeedbackTween = DOVirtual.Float(0f, 1f, duration, progress =>
                {
                    if (target == null || version != _settlementFeedbackVersion)
                    {
                        return;
                    }

                    ApplySettlementMotion(target, profile, progress);
                })
                .SetEase(Ease.Linear)
                .SetLink(target.gameObject);

            try
            {
                await PresentationTween.AwaitCompletionAsync(_settlementFeedbackTween, cancellationToken);
            }
            finally
            {
                if (version == _settlementFeedbackVersion)
                {
                    StopSettlementFeedback(restoreTransform: true);
                }
            }
        }

        private SettlementFeedbackProfile BuildSettlementFeedbackProfile(SettlementDishFeedbackKind kind)
        {
            switch (kind)
            {
                case SettlementDishFeedbackKind.DishChapterStarted:
                    return new SettlementFeedbackProfile(
                        duration: 0.18f,
                        anticipationScale: 0.95f,
                        peakScale: new Vector2(1.14f, 1.14f),
                        liftInCells: 0.04f,
                        sideInCells: 0f,
                        rotationDegrees: 0f,
                        rotationCycles: 0f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.92f),
                        glowWidth: 0.09f,
                        glowInflate: 1.07f,
                        glowFillAlpha: 0.045f,
                        glowPulseSpeed: 6f,
                        glowPulseAmplitude: 0.14f,
                        anticipationFraction: 0.18f);

                case SettlementDishFeedbackKind.DishChapterCompleted:
                    return new SettlementFeedbackProfile(
                        duration: 0.14f,
                        anticipationScale: 1f,
                        peakScale: new Vector2(1.10f, 1.10f),
                        liftInCells: 0.01f,
                        sideInCells: 0f,
                        rotationDegrees: 0f,
                        rotationCycles: 0f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.98f),
                        glowWidth: 0.11f,
                        glowInflate: 1.085f,
                        glowFillAlpha: 0.035f,
                        glowPulseSpeed: 8f,
                        glowPulseAmplitude: 0.18f);

                case SettlementDishFeedbackKind.DishBase:
                    return new SettlementFeedbackProfile(
                        duration: 0.14f,
                        anticipationScale: 1f,
                        peakScale: new Vector2(1.07f, 1.07f),
                        liftInCells: 0.02f,
                        sideInCells: 0f,
                        rotationDegrees: 0f,
                        rotationCycles: 0f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.72f),
                        glowWidth: 0.055f,
                        glowInflate: 1.035f,
                        glowFillAlpha: 0.02f,
                        glowPulseSpeed: 5f,
                        glowPulseAmplitude: 0.08f);

                case SettlementDishFeedbackKind.GenericSkillTriggered:
                    return new SettlementFeedbackProfile(
                        duration: 0.34f,
                        anticipationScale: 0.90f,
                        peakScale: new Vector2(1.24f, 1.24f),
                        liftInCells: 0.08f,
                        sideInCells: 0f,
                        rotationDegrees: 6f,
                        rotationCycles: 1.5f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.NativeSource, 0.95f),
                        glowWidth: 0.105f,
                        glowInflate: 1.08f,
                        glowFillAlpha: 0.08f,
                        glowPulseSpeed: 9f,
                        glowPulseAmplitude: 0.20f,
                        anticipationFraction: 0.20f);

                case SettlementDishFeedbackKind.TriggerSweetTransferActivatorPulse:
                    return new SettlementFeedbackProfile(
                        duration: 0.24f,
                        anticipationScale: 0.97f,
                        peakScale: new Vector2(1.08f, 1.05f),
                        liftInCells: 0.015f,
                        sideInCells: 0.035f,
                        rotationDegrees: 3.5f,
                        rotationCycles: 2f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.NativeSource, 0.98f),
                        glowWidth: 0.14f,
                        glowInflate: 1.12f,
                        glowFillAlpha: 0.07f,
                        glowPulseSpeed: 10f,
                        glowPulseAmplitude: 0.22f,
                        anticipationFraction: 0.10f);

                case SettlementDishFeedbackKind.SweetTransferSkillTriggered:
                    return new SettlementFeedbackProfile(
                        duration: 0.42f,
                        anticipationScale: 0.94f,
                        peakScale: new Vector2(1.19f, 1.12f),
                        liftInCells: 0.06f,
                        sideInCells: 0.10f,
                        rotationDegrees: 7f,
                        rotationCycles: 2f,
                        pulseCount: 2f,
                        glowColor: Color.clear,
                        glowWidth: 0f,
                        glowInflate: 1.09f,
                        glowFillAlpha: 0f,
                        glowPulseSpeed: 0f,
                        glowPulseAmplitude: 0f,
                        anticipationFraction: 0.12f,
                        glowInnerAlpha: 0f,
                        glowOuterAlpha: 0f,
                        glowIntensity: 0f);

                case SettlementDishFeedbackKind.SweetTransferResult:
                    return new SettlementFeedbackProfile(
                        duration: 0.30f,
                        anticipationScale: 0.96f,
                        peakScale: new Vector2(1.12f, 1.08f),
                        liftInCells: 0.035f,
                        sideInCells: 0.03f,
                        rotationDegrees: 4f,
                        rotationCycles: 1.5f,
                        pulseCount: 1f,
                        glowColor: Color.clear,
                        glowWidth: 0f,
                        glowInflate: 1.06f,
                        glowFillAlpha: 0f,
                        glowPulseSpeed: 0f,
                        glowPulseAmplitude: 0f,
                        anticipationFraction: 0.08f,
                        glowInnerAlpha: 0f,
                        glowOuterAlpha: 0f,
                        glowIntensity: 0f);

                case SettlementDishFeedbackKind.SweetTransferExecutor:
                    return new SettlementFeedbackProfile(
                        duration: 0.34f,
                        anticipationScale: 0.92f,
                        peakScale: new Vector2(1.18f, 1.14f),
                        liftInCells: 0.055f,
                        sideInCells: 0f,
                        rotationDegrees: 3.5f,
                        rotationCycles: 1.5f,
                        pulseCount: 1.5f,
                        glowColor: Color.clear,
                        glowWidth: 0f,
                        glowInflate: 1.075f,
                        glowFillAlpha: 0f,
                        glowPulseSpeed: 0f,
                        glowPulseAmplitude: 0f,
                        anticipationFraction: 0.12f,
                        glowInnerAlpha: 0f,
                        glowOuterAlpha: 0f,
                        glowIntensity: 0f);

                case SettlementDishFeedbackKind.SweetTransferFailed:
                    return new SettlementFeedbackProfile(
                        duration: 0.34f,
                        anticipationScale: 1.06f,
                        peakScale: new Vector2(0.91f, 0.96f),
                        liftInCells: -0.015f,
                        sideInCells: 0.075f,
                        rotationDegrees: 5f,
                        rotationCycles: 2.5f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.Failure, 0.42f),
                        glowWidth: 0.055f,
                        glowInflate: 1.035f,
                        glowFillAlpha: 0.015f,
                        glowPulseSpeed: 6f,
                        glowPulseAmplitude: 0.06f,
                        anticipationFraction: 0.20f);

                case SettlementDishFeedbackKind.CopiedSkillTriggered:
                    return new SettlementFeedbackProfile(
                        duration: 0.44f,
                        anticipationScale: 0.88f,
                        peakScale: new Vector2(1.20f, 1.20f),
                        liftInCells: 0.08f,
                        sideInCells: -0.04f,
                        rotationDegrees: 10f,
                        rotationCycles: 2f,
                        pulseCount: 2f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.CopiedSkillSource, 0.95f),
                        glowWidth: 0.12f,
                        glowInflate: 1.10f,
                        glowFillAlpha: 0.08f,
                        glowPulseSpeed: 13f,
                        glowPulseAmplitude: 0.24f,
                        anticipationFraction: 0.18f);

                case SettlementDishFeedbackKind.CopySkillTriggered:
                    return new SettlementFeedbackProfile(
                        duration: 0.46f,
                        anticipationScale: 0.92f,
                        peakScale: new Vector2(0.58f, 1.14f),
                        liftInCells: 0.05f,
                        sideInCells: 0f,
                        rotationDegrees: 4f,
                        rotationCycles: 2f,
                        pulseCount: 2f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.CopySkill, 0.95f),
                        glowWidth: 0.12f,
                        glowInflate: 1.10f,
                        glowFillAlpha: 0.10f,
                        glowPulseSpeed: 14f,
                        glowPulseAmplitude: 0.25f,
                        anticipationFraction: 0.18f);

                case SettlementDishFeedbackKind.PassiveFlatBonus:
                    return BonusProfile(
                        active: false,
                        peakScale: new Vector2(1.12f, 1.15f),
                        liftInCells: 0.055f,
                        sideInCells: 0f,
                        rotationDegrees: 1.5f,
                        color: SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.86f));

                case SettlementDishFeedbackKind.ActiveFlatBonus:
                    return BonusProfile(
                        active: true,
                        peakScale: new Vector2(1.14f, 1.25f),
                        liftInCells: 0.13f,
                        sideInCells: 0f,
                        rotationDegrees: 2.5f,
                        color: SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.96f));

                case SettlementDishFeedbackKind.PermanentFlatBonus:
                    return new SettlementFeedbackProfile(
                        duration: 0.46f,
                        anticipationScale: 0.88f,
                        peakScale: new Vector2(1.18f, 1.30f),
                        liftInCells: 0.16f,
                        sideInCells: 0f,
                        rotationDegrees: 1.5f,
                        rotationCycles: 1f,
                        pulseCount: 2f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.PermanentScore, 0.98f),
                        glowWidth: 0.13f,
                        glowInflate: 1.11f,
                        glowFillAlpha: 0.09f,
                        glowPulseSpeed: 11f,
                        glowPulseAmplitude: 0.24f,
                        anticipationFraction: 0.18f);

                case SettlementDishFeedbackKind.PassiveMultiplier:
                    return BonusProfile(
                        active: false,
                        peakScale: new Vector2(1.17f, 1.17f),
                        liftInCells: 0.03f,
                        sideInCells: 0f,
                        rotationDegrees: 5f,
                        color: SettlementColorPalette.WithAlpha(SettlementColorPalette.MultiplyMultiplier, 0.86f));

                case SettlementDishFeedbackKind.ActiveMultiplier:
                    return BonusProfile(
                        active: true,
                        peakScale: new Vector2(1.30f, 1.30f),
                        liftInCells: 0.05f,
                        sideInCells: 0f,
                        rotationDegrees: 9f,
                        color: SettlementColorPalette.WithAlpha(SettlementColorPalette.MultiplyMultiplier, 0.98f));

                case SettlementDishFeedbackKind.PassiveMultiplierAdd:
                    return BonusProfile(
                        active: false,
                        peakScale: new Vector2(1.17f, 1.06f),
                        liftInCells: 0.025f,
                        sideInCells: 0.04f,
                        rotationDegrees: 3f,
                        color: SettlementColorPalette.WithAlpha(SettlementColorPalette.AddMultiplier, 0.86f));

                case SettlementDishFeedbackKind.ActiveMultiplierAdd:
                    return BonusProfile(
                        active: true,
                        peakScale: new Vector2(1.28f, 1.09f),
                        liftInCells: 0.04f,
                        sideInCells: 0.065f,
                        rotationDegrees: 5f,
                        color: SettlementColorPalette.WithAlpha(SettlementColorPalette.AddMultiplier, 0.98f));

                case SettlementDishFeedbackKind.CakeLayerFlatBurst:
                    return new SettlementFeedbackProfile(
                        duration: 0.40f,
                        anticipationScale: 0.93f,
                        peakScale: new Vector2(1.20f, 1.22f),
                        liftInCells: 0.10f,
                        sideInCells: 0f,
                        rotationDegrees: 2f,
                        rotationCycles: 1f,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.BaseScore, 0.98f),
                        glowWidth: 0.12f,
                        glowInflate: 1.10f,
                        glowFillAlpha: 0.07f,
                        glowPulseSpeed: 10f,
                        glowPulseAmplitude: 0.22f,
                        anticipationFraction: 0.20f);

                case SettlementDishFeedbackKind.CakeLayerMultiplierAddBurst:
                    return new SettlementFeedbackProfile(
                        duration: 0.44f,
                        anticipationScale: 0.90f,
                        peakScale: new Vector2(1.26f, 1.11f),
                        liftInCells: 0.05f,
                        sideInCells: 0.07f,
                        rotationDegrees: 5f,
                        rotationCycles: 1.5f,
                        pulseCount: 1.5f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.AddMultiplier, 0.98f),
                        glowWidth: 0.135f,
                        glowInflate: 1.115f,
                        glowFillAlpha: 0.08f,
                        glowPulseSpeed: 12f,
                        glowPulseAmplitude: 0.25f,
                        anticipationFraction: 0.20f);

                case SettlementDishFeedbackKind.CakeLayerMultiplierBurst:
                    return new SettlementFeedbackProfile(
                        duration: 0.50f,
                        anticipationScale: 0.87f,
                        peakScale: new Vector2(1.34f, 1.34f),
                        liftInCells: 0.07f,
                        sideInCells: 0.035f,
                        rotationDegrees: 10f,
                        rotationCycles: 2f,
                        pulseCount: 2f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.MultiplyMultiplier, 1f),
                        glowWidth: 0.15f,
                        glowInflate: 1.13f,
                        glowFillAlpha: 0.10f,
                        glowPulseSpeed: 14f,
                        glowPulseAmplitude: 0.28f,
                        anticipationFraction: 0.22f);

                case SettlementDishFeedbackKind.CountAsChanged:
                    return new SettlementFeedbackProfile(
                        duration: 0.44f,
                        anticipationScale: 0.88f,
                        peakScale: new Vector2(1.28f, 1.34f),
                        liftInCells: 0.13f,
                        sideInCells: 0f,
                        rotationDegrees: 2f,
                        rotationCycles: 1f,
                        pulseCount: 2f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.CountAs, 0.98f),
                        glowWidth: 0.13f,
                        glowInflate: 1.11f,
                        glowFillAlpha: 0.08f,
                        glowPulseSpeed: 12f,
                        glowPulseAmplitude: 0.24f,
                        anticipationFraction: 0.18f);

                case SettlementDishFeedbackKind.TemporaryCategoryApplied:
                    return new SettlementFeedbackProfile(
                        duration: 0.48f,
                        anticipationScale: 0.84f,
                        peakScale: new Vector2(1.34f, 1.22f),
                        liftInCells: 0.10f,
                        sideInCells: 0.035f,
                        rotationDegrees: 8f,
                        rotationCycles: 2f,
                        pulseCount: 2f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.TemporaryCategory, 0.98f),
                        glowWidth: 0.14f,
                        glowInflate: 1.12f,
                        glowFillAlpha: 0.10f,
                        glowPulseSpeed: 13f,
                        glowPulseAmplitude: 0.25f,
                        anticipationFraction: 0.20f);

                case SettlementDishFeedbackKind.GenericValueChanged:
                default:
                    return new SettlementFeedbackProfile(
                        duration: Mathf.Max(_deliciousnessGainPunchDuration, _deliciousnessGainWobbleDuration),
                        anticipationScale: 1f,
                        peakScale: Vector2.one * _deliciousnessGainPunchScale,
                        liftInCells: 0.035f,
                        sideInCells: 0f,
                        rotationDegrees: _deliciousnessGainWobbleDegrees,
                        rotationCycles: _deliciousnessGainWobbleCycles,
                        pulseCount: 1f,
                        glowColor: SettlementColorPalette.WithAlpha(SettlementColorPalette.Special, 0.78f),
                        glowWidth: 0.07f,
                        glowInflate: 1.05f,
                        glowFillAlpha: 0.025f,
                        glowPulseSpeed: 7f,
                        glowPulseAmplitude: 0.12f);
            }
        }

        private static SettlementFeedbackProfile BonusProfile(
            bool active,
            Vector2 peakScale,
            float liftInCells,
            float sideInCells,
            float rotationDegrees,
            Color color)
        {
            return new SettlementFeedbackProfile(
                duration: active ? 0.36f : 0.27f,
                anticipationScale: active ? 0.90f : 1f,
                peakScale: peakScale,
                liftInCells: liftInCells,
                sideInCells: sideInCells,
                rotationDegrees: rotationDegrees,
                rotationCycles: active ? 1.5f : 1f,
                pulseCount: 1f,
                glowColor: color,
                glowWidth: active ? 0.115f : 0.075f,
                glowInflate: active ? 1.095f : 1.055f,
                glowFillAlpha: active ? 0.08f : 0.025f,
                glowPulseSpeed: active ? 10f : 7f,
                glowPulseAmplitude: active ? 0.22f : 0.12f,
                anticipationFraction: active ? 0.20f : 0f);
        }

        private void ApplySettlementMotion(Transform target, SettlementFeedbackProfile profile, float progress)
        {
            float t = Mathf.Clamp01(progress);
            float anticipationEnd = Mathf.Clamp(profile.AnticipationFraction, 0f, 0.45f);
            if (anticipationEnd > 0f && t < anticipationEnd)
            {
                float anticipation = Mathf.SmoothStep(0f, 1f, t / anticipationEnd);
                float scale = Mathf.Lerp(1f, profile.AnticipationScale, anticipation);
                target.localScale = Vector3.Scale(_settlementFeedbackBaseScale, new Vector3(scale, scale, 1f));
                target.localPosition = _settlementFeedbackBasePosition;
                target.localRotation = _settlementFeedbackBaseRotation;
                return;
            }

            float release = anticipationEnd < 1f
                ? Mathf.Clamp01((t - anticipationEnd) / (1f - anticipationEnd))
                : 1f;
            float settle = Mathf.SmoothStep(0f, 1f, release);
            float pulse = Mathf.Abs(Mathf.Sin(release * profile.PulseCount * Mathf.PI)) * (1f - release * 0.16f);
            float baseScale = Mathf.Lerp(profile.AnticipationScale, 1f, settle);
            float scaleX = baseScale + (profile.PeakScale.x - 1f) * pulse;
            float scaleY = baseScale + (profile.PeakScale.y - 1f) * pulse;
            target.localScale = Vector3.Scale(_settlementFeedbackBaseScale, new Vector3(scaleX, scaleY, 1f));

            float cell = Mathf.Max(0.01f, _cellSize);
            float lateral = Mathf.Sin(release * profile.PulseCount * Mathf.PI * 2f)
                * profile.SideInCells
                * cell
                * (1f - release);
            float lift = pulse * profile.LiftInCells * cell;
            target.localPosition = _settlementFeedbackBasePosition + new Vector3(lateral, lift, 0f);

            float rotation = Mathf.Sin(release * profile.RotationCycles * Mathf.PI * 2f)
                * profile.RotationDegrees
                * (1f - release);
            target.localRotation = _settlementFeedbackBaseRotation * Quaternion.Euler(0f, 0f, rotation);
        }

        private void ShowSettlementGlow(SettlementFeedbackProfile profile, float durationScale)
        {
            if (_placementGlow == null)
            {
                return;
            }

            _placementGlow.gameObject.SetActive(true);
            // 结算发光层绘制在食物本体上方。细长 sprite 若启用内部边缘或填充，
            // 大部分不透明像素都会被判成边缘，最终看起来像整块被染色。
            ConfigureOutlineGlowRenderer(
                _placementGlow,
                ref _placementGlowBlock,
                profile.GlowColor,
                profile.GlowWidth,
                fillAlpha: 0f,
                inflate: profile.GlowInflate,
                sortingOrderOffset: 2,
                materialOverride: null,
                pulseSpeed: profile.GlowPulseSpeed / Mathf.Max(0.05f, durationScale),
                pulseAmplitude: profile.GlowPulseAmplitude,
                innerAlpha: 0f,
                outerAlpha: profile.GlowOuterAlpha,
                glowIntensity: profile.GlowIntensity);
        }

        private void ShowSweetTransferSourceGlow()
        {
            // 角色身份由舞台聚光、方向轨迹和文案表达。轮廓 Shader 在细长食物上
            // 会把内部像素染成整块荧光色，因此持续状态不再覆盖食物本体。
            HidePlacementGlow();
        }

        private void ShowTriggerSweetTransferActivatorGlow()
        {
            if (_placementGlow == null)
            {
                return;
            }

            _placementGlow.gameObject.SetActive(true);
            ConfigureOutlineGlowRenderer(
                _placementGlow,
                ref _placementGlowBlock,
                SettlementColorPalette.WithAlpha(SettlementColorPalette.NativeSource, 0.98f),
                outlineWidth: 0.14f,
                fillAlpha: 0f,
                inflate: 1.12f,
                sortingOrderOffset: 4,
                materialOverride: null,
                pulseSpeed: 3.4f,
                pulseAmplitude: 0.24f,
                innerAlpha: 0f);
        }

        private void ShowSweetTransferExecutorGlow()
        {
            HidePlacementGlow();
        }

        private void StopSettlementFeedback(bool restoreTransform)
        {
            _settlementFeedbackTween?.Kill();
            _settlementFeedbackTween = null;
            _settlementFeedbackKind = SettlementDishFeedbackKind.None;

            if (restoreTransform && _settlementFeedbackTarget != null)
            {
                _settlementFeedbackTarget.localPosition = _settlementFeedbackBasePosition;
                _settlementFeedbackTarget.localScale = _settlementFeedbackBaseScale;
                _settlementFeedbackTarget.localRotation = _settlementFeedbackBaseRotation;
            }

            _settlementFeedbackTarget = null;
            if (_placementGlow != null)
            {
                if (_triggerSweetTransferActivatorActive && isActiveAndEnabled)
                {
                    ShowTriggerSweetTransferActivatorGlow();
                }
                else if (_sweetTransferExecutorActive && isActiveAndEnabled)
                {
                    ShowSweetTransferExecutorGlow();
                }
                else if (_sweetTransferSourceActive && isActiveAndEnabled)
                {
                    ShowSweetTransferSourceGlow();
                }
                else
                {
                    HidePlacementGlow();
                }
            }
        }

        private void RestorePersistentSweetTransferGlow()
        {
            if (_placementGlow == null)
            {
                return;
            }

            if (_triggerSweetTransferActivatorActive && isActiveAndEnabled)
            {
                ShowTriggerSweetTransferActivatorGlow();
            }
            else if (_sweetTransferExecutorActive && isActiveAndEnabled)
            {
                ShowSweetTransferExecutorGlow();
            }
            else if (_sweetTransferSourceActive && isActiveAndEnabled)
            {
                ShowSweetTransferSourceGlow();
            }
            else
            {
                HidePlacementGlow();
            }
        }

        private void HidePlacementGlow()
        {
            if (_placementGlow == null)
            {
                return;
            }

            _placementGlowBlock ??= new MaterialPropertyBlock();
            _placementGlowBlock.Clear();
            _placementGlowBlock.SetColor(OutlineColorId, Color.clear);
            _placementGlowBlock.SetFloat(OutlineWidthId, 0f);
            _placementGlowBlock.SetFloat(FillAlphaId, 0f);
            _placementGlowBlock.SetFloat(GlowIntensityId, 0f);
            _placementGlowBlock.SetFloat(OuterAlphaId, 0f);
            _placementGlowBlock.SetFloat(InnerAlphaId, 0f);
            _placementGlowBlock.SetFloat(PulseSpeedId, 0f);
            _placementGlowBlock.SetFloat(PulseAmplitudeId, 0f);
            _placementGlowBlock.SetFloat(PulseFrequencyId, 0f);
            _placementGlowBlock.SetFloat(UvInflateId, 1f);
            _placementGlowBlock.SetVector(SpriteUvRectId, Vector4.zero);
            _placementGlow.SetPropertyBlock(_placementGlowBlock);
            _placementGlow.gameObject.SetActive(false);
        }

        private void StopScopeAffectedShake(bool restoreTransform)
        {
            _scopeAffectedTween?.Kill();
            _scopeAffectedTween = null;

            if (restoreTransform && _scopeAffectedTarget != null)
            {
                _scopeAffectedTarget.localPosition = _scopeAffectedBasePosition;
            }

            _scopeAffectedTarget = null;
        }

        private readonly struct SettlementFeedbackProfile
        {
            public SettlementFeedbackProfile(
                float duration,
                float anticipationScale,
                Vector2 peakScale,
                float liftInCells,
                float sideInCells,
                float rotationDegrees,
                float rotationCycles,
                float pulseCount,
                Color glowColor,
                float glowWidth,
                float glowInflate,
                float glowFillAlpha,
                float glowPulseSpeed,
                float glowPulseAmplitude,
                float anticipationFraction = 0f,
                float glowInnerAlpha = 0.68f,
                float glowOuterAlpha = 1f,
                float glowIntensity = -1f)
            {
                Duration = duration;
                AnticipationScale = anticipationScale;
                PeakScale = peakScale;
                LiftInCells = liftInCells;
                SideInCells = sideInCells;
                RotationDegrees = rotationDegrees;
                RotationCycles = rotationCycles;
                PulseCount = pulseCount;
                GlowColor = glowColor;
                GlowWidth = glowWidth;
                GlowInflate = glowInflate;
                GlowFillAlpha = glowFillAlpha;
                GlowPulseSpeed = glowPulseSpeed;
                GlowPulseAmplitude = glowPulseAmplitude;
                AnticipationFraction = anticipationFraction;
                GlowInnerAlpha = glowInnerAlpha;
                GlowOuterAlpha = glowOuterAlpha;
                GlowIntensity = glowIntensity;
            }

            public float Duration { get; }
            public float AnticipationScale { get; }
            public Vector2 PeakScale { get; }
            public float LiftInCells { get; }
            public float SideInCells { get; }
            public float RotationDegrees { get; }
            public float RotationCycles { get; }
            public float PulseCount { get; }
            public Color GlowColor { get; }
            public float GlowWidth { get; }
            public float GlowInflate { get; }
            public float GlowFillAlpha { get; }
            public float GlowPulseSpeed { get; }
            public float GlowPulseAmplitude { get; }
            public float AnticipationFraction { get; }
            public float GlowInnerAlpha { get; }
            public float GlowOuterAlpha { get; }
            public float GlowIntensity { get; }
        }

        private void RebuildCells(DishShape shape)
        {
            EnsureRefs();
            if (_collider == null || _spriteRenderer == null || _shadowRenderer == null || _visualPivot == null)
            {
                return;
            }

            ConfigureFootprintSprite(shape);
            ConfigureContactShadow(shape);
            _dishValueBadgePresenter?.UpdateLayout(shape, _cellSize, _pitch);
            ApplyLiftHeight(_liftHeight);
            if (_dragPresentationActive)
            {
                ApplyDragPresentation();
            }

            _collider.size = new Vector2(
                Mathf.Max(_cellSize, shape.Width * _pitch - (_pitch - _cellSize)),
                Mathf.Max(_cellSize, shape.Height * _pitch - (_pitch - _cellSize)));
            _collider.offset = new Vector2((shape.Width - 1) * _pitch * 0.5f, -(shape.Height - 1) * _pitch * 0.5f);
        }

        /// <summary>
        /// 脚下绘本式接触阴影：直接复用食物 Sprite 的 Alpha 轮廓，因此 L/T 等异形食物
        /// 不会污染包围盒内的空格。核心层负责贴桌硬影，低透明外扩层只在举高/拖拽时显现。
        /// 两层都钉在地面脚印中心，不跟随本体视觉枢轴抬升。
        /// </summary>
        private void ConfigureContactShadow(DishShape shape)
        {
            Sprite shadowSprite = _spriteRenderer != null ? _spriteRenderer.sprite : _sprite;
            _shadowRenderer.sprite = shadowSprite;
            BattleSorting.Apply(
                _shadowRenderer,
                BattleSorting.Pieces,
                BattleSorting.OrderShadow + _sortingOrderOffset);
            _shadowRenderer.color = new Color(0f, 0f, 0f, _shadowBaseAlpha);
            SpriteRenderStyle.ApplyUnlitMaterial(_shadowRenderer);

            Transform body = _spriteRenderer.transform;
            float groundScale = Mathf.Max(0.0001f, _shadowGroundScale);
            _shadowBaseScale = Vector3.Scale(
                body.localScale,
                new Vector3(groundScale, groundScale, 1f));

            Transform t = _shadowRenderer.transform;
            t.localScale = _shadowBaseScale;
            t.localRotation = body.localRotation;
            _shadowRenderer.flipX = _spriteRenderer.flipX;
            _shadowRenderer.flipY = _spriteRenderer.flipY;

            Vector3 center = FootprintCenterLocal(shape);
            center.z = 0.05f;
            _shadowBaseLocalPos = center + new Vector3(
                _cellSize * _shadowGroundSide,
                -_cellSize * _shadowGroundDrop,
                0f);
            t.localPosition = _shadowBaseLocalPos;

            if (_shadowHaloRenderer != null)
            {
                _shadowHaloRenderer.sprite = shadowSprite;
                // 外扩影排在核心层之下（仍在所有食物本体之下）。
                BattleSorting.Apply(
                    _shadowHaloRenderer,
                    BattleSorting.Pieces,
                    BattleSorting.OrderShadow - 1 + _sortingOrderOffset);
                _shadowHaloRenderer.color = new Color(0f, 0f, 0f, 0f);
                SpriteRenderStyle.ApplyUnlitMaterial(_shadowHaloRenderer);
                Transform ht = _shadowHaloRenderer.transform;
                ht.localScale = _shadowBaseScale;
                ht.localPosition = _shadowBaseLocalPos;
                ht.localRotation = body.localRotation;
                _shadowHaloRenderer.flipX = _spriteRenderer.flipX;
                _shadowHaloRenderer.flipY = _spriteRenderer.flipY;
            }

            RefreshShadowBatchRegistration(topologyChanged: true);
        }

        private void ConfigureFootprintSprite(DishShape shape)
        {
            Transform pivot = _visualPivot != null ? _visualPivot : transform;
            Vector3 footprintCenter = FootprintCenterLocal(shape);
            Vector3 visualCenter = VisualPivotLocal(shape);
            _visualBaseLocalPos = visualCenter;
            pivot.localPosition = visualCenter;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            Transform t = _spriteRenderer.transform;
            t.localPosition = footprintCenter - visualCenter;

            _spriteRenderer.sprite = _sprite;
            BattleSorting.Apply(
                _spriteRenderer,
                _flying ? BattleSorting.PiecesFlying : BattleSorting.Pieces,
                BattleSorting.OrderBody + _sortingOrderOffset);
            _spriteRenderer.color = Color.white;
            SpriteRenderStyle.ApplyUnlitMaterial(_spriteRenderer);

            // sprite 按"基础朝向"绘制；摆放时若发生 90° 旋转，需把 sprite 一并旋转，
            // 并以基础朝向的占格尺寸做缩放，再旋转，才能贴格无缝且不被挤压。
            int rot = ((RotationIndex % 4) + 4) % 4;
            t.localScale = DishVisualLayout.SpriteScale(
                _sprite,
                shape,
                rot,
                _cellSize,
                _pitch);
            // DishShape.Rotate90 为顺时针；Unity +Z 为逆时针，故顺时针旋转取负角。
            t.localRotation = Quaternion.Euler(0f, 0f, -90f * rot);

            ApplyFlavorVisual();
            ApplyDebuffVisual();
            RefreshScopeTargetGlow();
        }

        internal void ApplyFlavorVisualToGraphic(Graphic graphic, Material flavorMaterial)
        {
            FlavorOrganicVisual.ApplyToGraphic(
                graphic,
                flavorMaterial,
                _spriteRenderer != null ? _spriteRenderer.sprite : null,
                Instance?.FlavorIds,
                Instance != null ? Instance.Id : 0f,
                _flavorVisualIntensity,
                useGlobalTime: true);
        }

        /// <summary>
        /// 按实例风味给本体应用有机味区；未知风味忽略、重复风味去重，
        /// 无可映射风味（或 shader 缺失）时回落到普通 Unlit。
        /// </summary>
        private void ApplyFlavorVisual()
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            FlavorOrganicVisual.ApplyToSpriteRenderer(
                _spriteRenderer,
                Instance?.FlavorIds,
                ref _flavorVisualBlock,
                Instance != null ? Instance.Id : 0f,
                _flavorVisualIntensity,
                useGlobalTime: true);
        }

        private void ApplyDebuffVisual()
        {
            if (_spriteRenderer == null
                || Instance == null
                || !Instance.ExcludedFromScore
                || _debuffVisualSuppressed)
            {
                return;
            }

            _spriteRenderer.SetPropertyBlock(null);
            DebuffVisualStyle.ApplyToSprite(_spriteRenderer);
        }

        private Vector3 FootprintCenterLocal(DishShape shape)
        {
            if (shape == null)
            {
                return Vector3.zero;
            }

            return new Vector3(
                (shape.Width - 1) * _pitch * 0.5f,
                -(shape.Height - 1) * _pitch * 0.5f,
                0f);
        }

        private Vector3 VisualPivotLocal(DishShape shape)
        {
            if (!_useOccupiedCentroidPivot || shape == null || shape.CellCount == 0)
            {
                return FootprintCenterLocal(shape);
            }

            Vector3 sum = Vector3.zero;
            foreach (GridPos cell in shape.Cells)
            {
                sum += new Vector3(cell.X * _pitch, -cell.Y * _pitch, 0f);
            }

            return sum / shape.CellCount;
        }

        /// <summary>
        /// 设置本体离地高度（世界单位）：0=贴桌，越大越高。
        /// 只抬升本体视觉枢轴，根节点保持贴格、阴影留在地面脚印中心；
        /// 阴影随高度轻微变大并淡出（核心层淡出 + 低透明外扩轮廓层显现）。
        /// </summary>
        public void SetLiftHeight(float worldHeight)
        {
            _liftHeight = Mathf.Max(0f, worldHeight);
            ApplyLiftHeight(_liftHeight);
        }

        /// <summary>便捷封装：lift01∈[0,1] 映射到参考举高高度（约 <see cref="_shadowLiftRefCells"/> 格）。</summary>
        public void SetLift(float lift01)
        {
            SetLiftHeight(Mathf.Clamp01(lift01) * RefLiftHeight());
        }

        private float RefLiftHeight()
        {
            return Mathf.Max(0.0001f, _shadowLiftRefCells * Mathf.Max(_cellSize, 0.0001f));
        }

        private void ApplyLiftHeight(float worldHeight)
        {
            // 本体抬升：根节点保持贴格，只把视觉枢轴沿世界 +Y 抬高。
            if (_visualPivot != null)
            {
                _visualPivot.localPosition = _visualBaseLocalPos + new Vector3(0f, worldHeight, 0f);
            }

            float t = Mathf.Clamp01(worldHeight / RefLiftHeight());

            // 锐利核心层：略放大、随高度淡出，位置始终钉在地面脚印中心。
            if (_shadowRenderer != null)
            {
                Transform st = _shadowRenderer.transform;
                st.localPosition = _shadowBaseLocalPos;
                float coreScale = Mathf.Lerp(1f, _coreGrow, t);
                st.localScale = new Vector3(_shadowBaseScale.x * coreScale, _shadowBaseScale.y * coreScale, 1f);
                Color c = _shadowRenderer.color;
                c.a = _shadowBaseAlpha * Mathf.Lerp(1f, _coreFadeWhenHigh, t);
                _shadowRenderer.color = c;
            }

            // 外扩轮廓层：只轻微放大，贴桌时隐形，举高后以低透明度显现。
            if (_shadowHaloRenderer != null)
            {
                Transform ht = _shadowHaloRenderer.transform;
                ht.localPosition = _shadowBaseLocalPos;
                float haloScale = Mathf.Lerp(1f, _haloGrow, t);
                ht.localScale = new Vector3(_shadowBaseScale.x * haloScale, _shadowBaseScale.y * haloScale, 1f);
                Color hc = _shadowHaloRenderer.color;
                hc.a = Mathf.Lerp(0f, _haloAlphaWhenHigh, t);
                _shadowHaloRenderer.color = hc;
            }

            _shadowBatchRenderer?.MarkStreamDirty(this);
        }

        private void ApplyDragPresentation()
        {
            Transform target = VisualAnimationTarget();
            if (target != null)
            {
                target.localPosition = _visualBaseLocalPos;
                target.localRotation = Quaternion.identity;
                float scale = Mathf.Max(0.0001f, _dragVisualScale);
                target.localScale = new Vector3(scale, scale, 1f);
            }

            float targetScale = Mathf.Max(0.0001f, _dragShadowCellScale);
            float targetCellSize = Mathf.Max(0.0001f, _cellSize) * targetScale;
            Vector3 dragShadowPosition = FootprintCenterLocal(CurrentShape);
            dragShadowPosition.z = _shadowBaseLocalPos.z;
            dragShadowPosition += new Vector3(
                targetCellSize * _dragShadowSideCells,
                -targetCellSize * _dragShadowDropCells,
                0f);
            if (_shadowRenderer != null)
            {
                _shadowRenderer.transform.localPosition = dragShadowPosition;
                _shadowRenderer.transform.localScale = new Vector3(
                    _shadowBaseScale.x * targetScale * _dragShadowCoreScale,
                    _shadowBaseScale.y * targetScale * _dragShadowCoreScale,
                    1f);
                Color color = _shadowRenderer.color;
                color.a = Mathf.Clamp01(_dragShadowCoreAlpha);
                _shadowRenderer.color = color;
                BattleSorting.Apply(
                    _shadowRenderer,
                    BattleSorting.Pieces,
                    BattleSorting.OrderShadow + _sortingOrderOffset);
            }

            if (_shadowHaloRenderer != null)
            {
                _shadowHaloRenderer.transform.localPosition = dragShadowPosition;
                _shadowHaloRenderer.transform.localScale = new Vector3(
                    _shadowBaseScale.x * targetScale * _dragShadowHaloScale,
                    _shadowBaseScale.y * targetScale * _dragShadowHaloScale,
                    1f);
                Color color = _shadowHaloRenderer.color;
                color.a = Mathf.Clamp01(_dragShadowHaloAlpha);
                _shadowHaloRenderer.color = color;
                BattleSorting.Apply(
                    _shadowHaloRenderer,
                    BattleSorting.Pieces,
                    BattleSorting.OrderShadow - 1 + _sortingOrderOffset);
            }

            if (_spriteRenderer != null)
            {
                foreach (SpriteRenderer renderer in _spriteRenderer.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    renderer.sortingLayerName = BattleSorting.PiecesFlying;
                }

                _spriteRenderer.sortingOrder = BattleSorting.OrderBody + _sortingOrderOffset;
                if (_placementGlow != null)
                {
                    _placementGlow.sortingOrder = _spriteRenderer.sortingOrder + 1;
                }
                if (_scopeTargetGlow != null)
                {
                    _scopeTargetGlow.sortingOrder = _spriteRenderer.sortingOrder + 3;
                }
            }

            _shadowBatchRenderer?.MarkStreamDirty(this);
        }

        private void RefreshShadowBatchRegistration(bool topologyChanged)
        {
            bool eligible = _shadowBatchRenderer != null
                && isActiveAndEnabled
                && !_flying
                && _sortingOrderOffset == 0
                && _shadowRenderer != null
                && _shadowHaloRenderer != null
                && _shadowRenderer.sprite != null
                && _shadowHaloRenderer.sprite != null;
            if (!eligible)
            {
                UnregisterFromShadowBatch();
                return;
            }

            if (!_shadowBatchRegistered && !_shadowBatchRenderer.Register(this))
            {
                SetFallbackShadowRendering(true);
                return;
            }

            if (topologyChanged)
            {
                _shadowBatchRenderer.MarkTopologyDirty(this);
            }
            else
            {
                _shadowBatchRenderer.MarkStreamDirty(this);
            }
        }

        private void UnregisterFromShadowBatch()
        {
            if (_shadowBatchRegistered && _shadowBatchRenderer != null)
            {
                _shadowBatchRenderer.Unregister(this);
            }
            else
            {
                _shadowBatchRegistered = false;
                SetFallbackShadowRendering(true);
            }
        }

        private void SetFallbackShadowRendering(bool enabled)
        {
            if (_shadowRenderer != null)
            {
                _shadowRenderer.forceRenderingOff = !enabled;
            }

            if (_shadowHaloRenderer != null)
            {
                _shadowHaloRenderer.forceRenderingOff = !enabled;
            }
        }

        /// <summary>解析 prefab 预拼的渲染体与碰撞盒；缺失时只报错，不运行时补齐。</summary>
        private void EnsureRefs()
        {
            if (_collider == null)
            {
                _collider = GetComponent<BoxCollider2D>();
            }

            _visualPivot = ResolveChildTransform(_visualPivot, "VisualPivot");
            _shadowRenderer = ResolveChildRenderer(_shadowRenderer, "Shadow");
            _shadowHaloRenderer = ResolveChildRenderer(_shadowHaloRenderer, "ShadowHalo");
            _spriteRenderer = ResolveChildRenderer(_spriteRenderer, "Sprite");
            _placementGlow = ResolveChildRenderer(_placementGlow, "PlacementGlow");
            _scopeTargetGlow = ResolveChildRenderer(_scopeTargetGlow, "ScopeTargetGlow");
            EnsureSpriteUnderVisualPivot();

            if (_collider == null
                || _visualPivot == null
                || _shadowRenderer == null
                || _spriteRenderer == null
                || _placementGlow == null
                || _scopeTargetGlow == null)
            {
                Debug.LogError($"{nameof(DishPieceView)} prefab 缺少固定结构：BoxCollider2D/VisualPivot/Sprite/Shadow/PlacementGlow/ScopeTargetGlow。", this);
            }
        }

        private SpriteRenderer ResolveChildRenderer(SpriteRenderer current, string childName)
        {
            if (current != null)
            {
                return current;
            }

            Transform t = ResolveChildTransform(null, childName);
            if (t == null)
            {
                return null;
            }

            SpriteRenderer renderer = t.GetComponent<SpriteRenderer>();
            return renderer;
        }

        private void ConfigureOutlineGlowRenderer(
            SpriteRenderer renderer,
            ref MaterialPropertyBlock block,
            Color color,
            float outlineWidth,
            float fillAlpha,
            float inflate,
            int sortingOrderOffset,
            Material materialOverride,
            float pulseSpeed,
            float pulseAmplitude,
            float innerAlpha = 0f,
            float outerAlpha = 1f,
            float glowIntensity = -1f)
        {
            if (renderer == null || _spriteRenderer == null)
            {
                return;
            }

            Transform source = _spriteRenderer.transform;
            Transform target = renderer.transform;
            if (target.parent == source.parent)
            {
                target.localPosition = source.localPosition;
                target.localRotation = source.localRotation;
                target.localScale = new Vector3(
                    source.localScale.x * Mathf.Max(0.01f, inflate),
                    source.localScale.y * Mathf.Max(0.01f, inflate),
                    source.localScale.z);
            }
            else
            {
                target.position = source.position;
                target.rotation = source.rotation;
                Vector3 sourceLossy = source.lossyScale;
                Vector3 parentLossy = target.parent != null ? target.parent.lossyScale : Vector3.one;
                target.localScale = new Vector3(
                    SafeDivide(sourceLossy.x * Mathf.Max(0.01f, inflate), parentLossy.x),
                    SafeDivide(sourceLossy.y * Mathf.Max(0.01f, inflate), parentLossy.y),
                    SafeDivide(sourceLossy.z, parentLossy.z));
            }

            renderer.sprite = _spriteRenderer.sprite;
            renderer.sortingLayerName = _spriteRenderer.sortingLayerName;
            renderer.sortingOrder = _spriteRenderer.sortingOrder + sortingOrderOffset;

            Material material = materialOverride != null
                ? materialOverride
                : (_outlineGlowMaterial != null ? _outlineGlowMaterial : SpriteRenderStyle.SpriteOutlineMaterial);
            if (material != null)
            {
                renderer.sharedMaterial = material;
                renderer.color = materialOverride != null ? color : Color.white;
            }
            else
            {
                SpriteRenderStyle.ApplyUnlitMaterial(renderer);
                renderer.color = color;
            }

            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(OutlineColorId, color);
            block.SetFloat(OutlineWidthId, Mathf.Clamp(outlineWidth, 0f, 0.2f));
            block.SetFloat(FillAlphaId, Mathf.Clamp01(fillAlpha));
            block.SetFloat(GlowIntensityId, glowIntensity >= 0f ? glowIntensity : _outlineGlowIntensity);
            block.SetFloat(OuterAlphaId, Mathf.Clamp01(outerAlpha));
            block.SetFloat(InnerAlphaId, Mathf.Clamp01(innerAlpha));
            block.SetFloat(PulseSpeedId, Mathf.Max(0f, pulseSpeed));
            block.SetFloat(PulseAmplitudeId, Mathf.Clamp(pulseAmplitude, 0f, 0.5f));
            block.SetFloat(PulseFrequencyId, Mathf.Max(0f, _pulseFrequency));
            block.SetFloat(UvInflateId, Mathf.Max(1f, inflate));
            block.SetVector(SpriteUvRectId, SpriteUvRect(_spriteRenderer.sprite));
            renderer.SetPropertyBlock(block);
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

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
        }

        private Transform ResolveChildTransform(Transform current, string childName)
        {
            if (current != null)
            {
                return current;
            }

            Transform direct = transform.Find(childName);
            if (direct != null)
            {
                return direct;
            }

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private void EnsureSpriteUnderVisualPivot()
        {
            if (_visualPivot == null || _spriteRenderer == null || _spriteRenderer.transform.parent == _visualPivot)
            {
                return;
            }

            _spriteRenderer.transform.SetParent(_visualPivot, false);
        }

        private Transform VisualAnimationTarget()
        {
            return _visualPivot != null ? _visualPivot : (_spriteRenderer != null ? _spriteRenderer.transform : transform);
        }

        private void Update()
        {
            if (_suppressPrimaryUntilReleased)
            {
                SetHovered(false);
                if (WorldInput.PrimaryHeld || WorldInput.PrimaryPressedThisFrame)
                {
                    return;
                }

                _suppressPrimaryUntilReleased = false;
            }

            if (_moveDragging)
            {
                SetHovered(false);
                Vector2 screen = WorldInput.MouseScreen;
                if (WorldInput.PrimaryReleasedThisFrame || !WorldInput.PrimaryHeld)
                {
                    _moveDragging = false;
                    _moveEnd?.Invoke(screen);
                    return;
                }

                _moveUpdate?.Invoke(screen);
                return;
            }

            UpdateHover();

            if (!_clickEnabled || Instance == null || !WorldInput.PrimaryPressedThisFrame || _collider == null)
            {
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector2 world = WorldInput.MouseWorld(cam);
            if (AcceptsPointerAtWorldPoint(world))
            {
                if (_moveBegin != null)
                {
                    _moveDragging = true;
                    SetHovered(false);
                    _moveBegin.Invoke(this, WorldInput.MouseScreen);
                    return;
                }

                _clicked?.Invoke(Instance);
            }
        }

        private void UpdateHover()
        {
            if (!_clickEnabled || Instance == null || _collider == null)
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

            if (WorldInput.PointerOverUi)
            {
                SetHovered(false);
                return;
            }

            bool pointerInside = AcceptsPointerAtWorldPoint(WorldInput.MouseWorld(cam));
            SetHovered(pointerInside);
        }

        internal bool AcceptsPointerAtWorldPoint(Vector2 world)
        {
            return ContainsOccupiedCellAtWorldPoint(world)
                && (_pointerHitFilter == null || _pointerHitFilter(this, world));
        }

        private bool ContainsOccupiedCellAtWorldPoint(Vector2 world)
        {
            if (_collider == null || !_collider.OverlapPoint(world))
            {
                return false;
            }

            if (CurrentShape == null || CurrentShape.CellCount == 0)
            {
                return true;
            }

            Vector3 local = transform.InverseTransformPoint(new Vector3(world.x, world.y, transform.position.z));
            float half = _cellSize * 0.5f;
            const float epsilon = 0.0001f;
            foreach (GridPos cell in CurrentShape.Cells)
            {
                float centerX = cell.X * _pitch;
                float centerY = -cell.Y * _pitch;
                if (local.x >= centerX - half - epsilon
                    && local.x <= centerX + half + epsilon
                    && local.y >= centerY - half - epsilon
                    && local.y <= centerY + half + epsilon)
                {
                    return true;
                }
            }

            return false;
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

        internal void PrepareForReuse()
        {
            ResetReusableState(clearInstance: false, clearBadgeBinding: false);
        }

        internal void ResetForPool()
        {
            ResetReusableState(clearInstance: true, clearBadgeBinding: true);
            UnregisterFromShadowBatch();
        }

        private void ResetReusableState(bool clearInstance, bool clearBadgeBinding)
        {
            EnsureRefs();
            transform.DOKill(false);
            _visualPivot?.DOKill(false);
            _spriteRenderer?.transform.DOKill(false);
            _activeItemFlavorSequence?.Kill(false);
            _activeItemFlavorSequence = null;
            ApplyActiveItemTransformEffect(0f);
            _scopeAffectedVersion++;
            StopScopeAffectedShake(restoreTransform: true);
            _settlementFeedbackVersion++;
            StopSettlementFeedback(restoreTransform: true);
            _dishValueBadgePresenter?.ResetForReuse(clearBadgeBinding);
            ClearAllScopeTargetGlows();
            ClearSweetTransferBuffMarkers();
            ClearSettlementFocus();
            SetActiveItemTargetDimmed(false);
            SetDragPresentation(false);
            SetPlacementGlow(false, false);
            SetGhost(false);
            SetBodyAlpha(1f);
            _sortingOrderOffset = 0;
            SetFlying(false);
            _clicked = null;
            _hoverEntered = null;
            _hoverExited = null;
            _moveBegin = null;
            _moveUpdate = null;
            _moveEnd = null;
            _pointerHitFilter = null;
            _moveDragging = false;
            _clickEnabled = true;
            _suppressPrimaryUntilReleased = false;
            _debuffVisualSuppressed = false;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            SetHovered(false);

            if (!clearInstance)
            {
                return;
            }

            Instance = null;
            CurrentShape = null;
            RotationIndex = 0;
            _sprite = null;
            _cellSize = 0f;
            _pitch = 0f;
        }

        private void OnEnable()
        {
            RefreshShadowBatchRegistration(topologyChanged: true);
        }

        private void OnDisable()
        {
            UnregisterFromShadowBatch();
            ClearAllScopeTargetGlows();
            ClearSweetTransferBuffMarkers();
            ClearSettlementFocus();
            SetActiveItemTargetDimmed(false);
            if (_activeItemFlavorSequence != null)
            {
                _activeItemFlavorSequence.Kill();
                _activeItemFlavorSequence = null;
                ApplyActiveItemTransformEffect(0f);
                ApplyFlavorVisual();
            }

            _sweetTransferSourceActive = false;
            _sweetTransferExecutorActive = false;
            _triggerSweetTransferActivatorActive = false;
            _scopeAffectedVersion++;
            StopScopeAffectedShake(restoreTransform: true);
            _settlementFeedbackVersion++;
            StopSettlementFeedback(restoreTransform: true);
            SetHovered(false);
        }
    }
}
