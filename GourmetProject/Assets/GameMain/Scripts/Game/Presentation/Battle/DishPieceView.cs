using System;
using System.Collections.Generic;
using System.Threading;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 已摆放菜品表现：固定结构（接触阴影 + 菜品本体 + 碰撞盒）预拼在 prefab 上，由 <see cref="BuildPlaced"/> 喂数据。
    /// sprite/缩放/旋转/碰撞尺寸随形状(1x1/2x1/L/T...)与朝向变化，必须运行时计算（见 dish-footprint-sprite 规则）。
    /// 阴影一律走假阴影软暗斑（见 battle-fake-shadow 规则），全程 Unlit 平涂，不依赖 Light2D。
    /// </summary>
    public sealed class DishPieceView : MonoBehaviour
    {
        [Header("接触阴影：贴桌态（偏移按单格尺寸取比例，适配不同餐桌缩放）")]
        [SerializeField] private float _shadowBaseAlpha = 0.5f;
        [SerializeField] private float _shadowGroundScale = 1.22f;
        [SerializeField] private float _shadowGroundDrop = 0.16f;
        [SerializeField] private float _shadowGroundSide = 0.06f;

        [Header("接触阴影：举高态（按本体离地高度连续：越高越大、越淡、越虚）")]
        [Tooltip("阴影达到最大扩散/虚化的参考高度（按单格尺寸倍数，适配不同餐桌缩放）。")]
        [SerializeField] private float _shadowLiftRefCells = 1.5f;
        [Tooltip("锐利核心层在最高处的放大倍数。")]
        [SerializeField] private float _coreGrow = 1.15f;
        [Tooltip("锐利核心层在最高处的透明度乘子（越小越淡）。")]
        [SerializeField] private float _coreFadeWhenHigh = 0.15f;
        [Tooltip("弥散光晕层在最高处的放大倍数。")]
        [SerializeField] private float _haloGrow = 2.0f;
        [Tooltip("弥散光晕层在最高处的透明度（绝对值，贴桌时为 0）。")]
        [SerializeField] private float _haloAlphaWhenHigh = 0.28f;

        [Header("固定结构（prefab 预拼，运行时引用）")]
        [Tooltip("菜品本体渲染体（子物体 Sprite 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [Tooltip("菜品本体动画枢轴（子物体 VisualPivot）。多格菜的缩放/晃动绕这里执行，根节点保持贴格。")]
        [SerializeField] private Transform _visualPivot;
        [Tooltip("脚下接触阴影锐利核心层（子物体 Shadow 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _shadowRenderer;
        [Tooltip("脚下接触阴影弥散光晕层（子物体 ShadowHalo 上的 SpriteRenderer），高空时显现做虚化。")]
        [SerializeField] private SpriteRenderer _shadowHaloRenderer;
        [Tooltip("点击命中碰撞盒（prefab 根节点上的 BoxCollider2D）。")]
        [SerializeField] private BoxCollider2D _collider;
        [Tooltip("放置合法性发光层（子物体 PlacementGlow 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _placementGlow;

        [Header("落定反馈（仅作用于本体视觉枢轴，不影响格子锚点/碰撞盒）")]
        [SerializeField] private bool _useOccupiedCentroidPivot = true;
        [SerializeField] private float _landPunchScale = 1.12f;
        [SerializeField] private float _landPunchDuration = 0.16f;
        [SerializeField] private float _landWobbleDegrees = 4f;
        [SerializeField] private float _landWobbleDuration = 0.18f;
        [SerializeField] private float _landWobbleCycles = 1.5f;

        [Header("上菜落格砰反馈（仅缩放）")]
        [SerializeField] private float _serveLandImpactScale = 1.02f;
        [SerializeField] private float _serveLandImpactDuration = 0.14f;

        [Header("风味脏印（程序化噪声，仅作用于本体）")]
        [Tooltip("噪声频率：越大斑点越碎密。")]
        [SerializeField] private float _stainScale = 8f;
        [Tooltip("斑点阈值：越大脏印越稀疏、越散开。")]
        [SerializeField, Range(0f, 1f)] private float _stainThreshold = 0.62f;
        [Tooltip("斑点边缘软度。")]
        [SerializeField, Range(0.001f, 0.5f)] private float _stainSoftness = 0.12f;
        [Tooltip("脏印处对底色的压暗强度（越大越脏，不发亮）。")]
        [SerializeField, Range(0f, 1f)] private float _stainDarken = 0.12f;

        [Header("结算标签反馈：美味度增加（仅作用于本体视觉枢轴）")]
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
        private Vector3 _visualBaseLocalPos;
        private Action<DishInstance> _clicked;
        private Action<DishPieceView> _hoverEntered;
        private Action<DishPieceView> _hoverExited;
        private bool _clickEnabled = true;
        private bool _hovered;
        private const int MaxStains = 4;
        private static readonly int StainCountId = Shader.PropertyToID("_StainCount");
        private static readonly int StainScaleId = Shader.PropertyToID("_StainScale");
        private static readonly int StainThresholdId = Shader.PropertyToID("_StainThreshold");
        private static readonly int StainSoftnessId = Shader.PropertyToID("_StainSoftness");
        private static readonly int StainDarkenId = Shader.PropertyToID("_StainDarken");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int[] StainColorIds =
        {
            Shader.PropertyToID("_StainColor0"),
            Shader.PropertyToID("_StainColor1"),
            Shader.PropertyToID("_StainColor2"),
            Shader.PropertyToID("_StainColor3"),
        };

        private MaterialPropertyBlock _stainBlock;
        private readonly List<Color> _stainColorScratch = new(MaxStains);

        public DishInstance Instance { get; private set; }

        public int RotationIndex { get; private set; }

        public DishShape CurrentShape { get; private set; }

        public void BuildPlaced(DishInstance instance, Sprite sprite, float cellSize, float pitch, Action<DishInstance> clicked)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _sprite = sprite;
            _cellSize = cellSize;
            _pitch = pitch;
            _clicked = clicked;
            RotationIndex = instance.Placement.RotationIndex;
            CurrentShape = instance.Placement.Orientation;
            RebuildCells(CurrentShape);
        }

        /// <summary>是否响应普通点击（打开详情）。食物调整态下关闭，改由 FoodAdjustController 自行命中处理。</summary>
        public void SetClickEnabled(bool enabled)
        {
            _clickEnabled = enabled;
            if (!enabled)
            {
                SetHovered(false);
            }
        }

        public void SetHoverCallbacks(Action<DishPieceView> entered, Action<DishPieceView> exited)
        {
            _hoverEntered = entered;
            _hoverExited = exited;
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

        /// <summary>
        /// 放置可否的外轮廓发光：食物调整移动态给「光标菜品」显示——绿=可放，红=不可放；关闭则隐藏。
        /// 用一层复制本体 sprite、略放大并染色的描边层实现。
        /// </summary>
        public void SetPlacementGlow(bool show, bool valid)
        {
            EnsureRefs();
            if (!show)
            {
                if (_placementGlow != null)
                {
                    _placementGlow.gameObject.SetActive(false);
                }

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
            _placementGlow.sprite = _spriteRenderer.sprite;
            _placementGlow.transform.localPosition = Vector3.zero;
            _placementGlow.transform.localRotation = Quaternion.identity;
            _placementGlow.transform.localScale = Vector3.one * 1.16f;
            _placementGlow.sortingLayerName = _spriteRenderer.sortingLayerName;
            _placementGlow.sortingOrder = _spriteRenderer.sortingOrder - 1;
            _placementGlow.color = valid
                ? new Color(0.30f, 1f, 0.42f, 0.9f)
                : new Color(1f, 0.32f, 0.30f, 0.9f);
        }

        public void SetGhost(bool ghost)
        {
            EnsureRefs();
            if (_spriteRenderer == null)
            {
                return;
            }

            // 只调本体（含子）透明度；阴影核心/光晕的 alpha 由高度逻辑统一管理，别在这里覆盖。
            foreach (SpriteRenderer renderer in _spriteRenderer.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Color color = renderer.color;
                color.a = ghost ? Mathf.Min(color.a, 0.65f) : Mathf.Max(color.a, 0.95f);
                renderer.color = color;
            }
        }

        /// <summary>
        /// 飞入餐桌时只把<b>本体</b>切到 PiecesFlying 层，压在已摆放食品之上；
        /// 接触阴影（核心+光晕）始终留在 Pieces 地面层、排在所有菜本体之下，
        /// 这样下落途中阴影铺在桌面、被沿途菜品遮挡，不会盖出脏暗斑。落定后本体切回 Pieces 层。
        /// </summary>
        public void SetFlying(bool flying)
        {
            EnsureRefs();
            string layer = flying ? BattleSorting.PiecesFlying : BattleSorting.Pieces;
            if (_spriteRenderer != null)
            {
                foreach (SpriteRenderer renderer in _spriteRenderer.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    renderer.sortingLayerName = layer;
                }
            }

            if (_shadowRenderer != null)
            {
                _shadowRenderer.sortingLayerName = BattleSorting.Pieces;
            }

            if (_shadowHaloRenderer != null)
            {
                _shadowHaloRenderer.sortingLayerName = BattleSorting.Pieces;
            }
        }

        /// <summary>只缩放菜品本体视觉枢轴，不改变根节点格子锚点、阴影计算和碰撞盒。</summary>
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

        public Awaitable PlayServeLandImpactFeedbackAsync(CancellationToken cancellationToken)
        {
            EnsureRefs();
            return PresentationTween.PunchLocalScaleAsync(
                VisualAnimationTarget(),
                _serveLandImpactScale,
                _serveLandImpactDuration,
                cancellationToken);
        }

        public Awaitable PlayDeliciousnessGainFeedbackAsync(CancellationToken cancellationToken)
        {
            EnsureRefs();
            return PresentationTween.PunchScaleAndWobbleAsync(
                VisualAnimationTarget(),
                _deliciousnessGainPunchScale,
                _deliciousnessGainPunchDuration,
                _deliciousnessGainWobbleDegrees,
                _deliciousnessGainWobbleCycles,
                _deliciousnessGainWobbleDuration,
                cancellationToken);
        }

        private void RebuildCells(DishShape shape)
        {
            EnsureRefs();
            if (_collider == null || _spriteRenderer == null || _shadowRenderer == null || _visualPivot == null)
            {
                return;
            }

            ConfigureContactShadow(shape);
            ConfigureFootprintSprite(shape);
            ApplyLiftHeight(_liftHeight);

            _collider.size = new Vector2(
                Mathf.Max(_cellSize, shape.Width * _pitch - (_pitch - _cellSize)),
                Mathf.Max(_cellSize, shape.Height * _pitch - (_pitch - _cellSize)));
            _collider.offset = new Vector2((shape.Width - 1) * _pitch * 0.5f, -(shape.Height - 1) * _pitch * 0.5f);
        }

        /// <summary>
        /// 脚下软边接触阴影：用径向羽化暗斑铺满整个脚印，不依赖菜品图留白，必定可见。
        /// 分两层——锐利核心层（贴桌接触）+ 弥散光晕层（高空虚化），都钉在地面脚印中心。
        /// </summary>
        private void ConfigureContactShadow(DishShape shape)
        {
            Sprite blob = BattleShadow.SoftShadowSprite;
            _shadowRenderer.sprite = blob;
            BattleSorting.Apply(_shadowRenderer, BattleSorting.Pieces, BattleSorting.OrderShadow);
            _shadowRenderer.color = new Color(0f, 0f, 0f, _shadowBaseAlpha);
            SpriteRenderStyle.ApplyUnlitMaterial(_shadowRenderer);

            // 阴影覆盖旋转后的实际占格脚印（软边自然探出本体轮廓），无需随朝向旋转。
            float spanX = (shape.Width - 1) * _pitch + _cellSize;
            float spanY = (shape.Height - 1) * _pitch + _cellSize;
            Vector2 bounds = blob != null ? (Vector2)blob.bounds.size : Vector2.one;
            float sx = bounds.x > 0f ? spanX / bounds.x : spanX;
            float sy = bounds.y > 0f ? spanY / bounds.y : spanY;
            _shadowBaseScale = new Vector3(sx * _shadowGroundScale, sy * _shadowGroundScale, 1f);

            Transform t = _shadowRenderer.transform;
            t.localScale = _shadowBaseScale;

            Vector3 center = new Vector3(
                (shape.Width - 1) * _pitch * 0.5f,
                -(shape.Height - 1) * _pitch * 0.5f,
                0.05f);
            _shadowBaseLocalPos = center + new Vector3(_cellSize * _shadowGroundSide, -_cellSize * _shadowGroundDrop, 0f);
            t.localPosition = _shadowBaseLocalPos;

            if (_shadowHaloRenderer != null)
            {
                _shadowHaloRenderer.sprite = BattleShadow.DiffuseShadowSprite;
                // 光晕排在核心层之下（仍在所有菜本体之下），保证锐利核心压在弥散光晕之上。
                BattleSorting.Apply(_shadowHaloRenderer, BattleSorting.Pieces, BattleSorting.OrderShadow - 1);
                _shadowHaloRenderer.color = new Color(0f, 0f, 0f, 0f);
                SpriteRenderStyle.ApplyUnlitMaterial(_shadowHaloRenderer);
                Transform ht = _shadowHaloRenderer.transform;
                ht.localScale = _shadowBaseScale;
                ht.localPosition = _shadowBaseLocalPos;
            }
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
            BattleSorting.Apply(_spriteRenderer, BattleSorting.Pieces, BattleSorting.OrderBody);
            _spriteRenderer.color = Color.white;
            SpriteRenderStyle.ApplyUnlitMaterial(_spriteRenderer);

            // sprite 按"基础朝向"绘制；摆放时若发生 90° 旋转，需把 sprite 一并旋转，
            // 并以基础朝向的占格尺寸做缩放，再旋转，才能贴格无缝且不被挤压。
            int rot = ((RotationIndex % 4) + 4) % 4;
            bool swapped = (rot % 2) == 1;
            int baseW = swapped ? shape.Height : shape.Width;
            int baseH = swapped ? shape.Width : shape.Height;

            // 基础朝向下 sprite 应占据的世界跨度：(格数-1)*pitch + cellSize，gap=0 即 格数*cellSize。
            float spanX = (baseW - 1) * _pitch + _cellSize;
            float spanY = (baseH - 1) * _pitch + _cellSize;
            Vector2 bounds = _sprite != null ? (Vector2)_sprite.bounds.size : Vector2.one;
            float scaleX = bounds.x > 0f ? spanX / bounds.x : 1f;
            float scaleY = bounds.y > 0f ? spanY / bounds.y : 1f;
            t.localScale = new Vector3(scaleX, scaleY, 1f);
            // DishShape.Rotate90 为顺时针；Unity +Z 为逆时针，故顺时针旋转取负角。
            t.localRotation = Quaternion.Euler(0f, 0f, -90f * rot);

            ApplyFlavorStain();
        }

        /// <summary>
        /// 按实例风味给本体叠加脏印：去重取前若干种风味色，走 FlavorStain 材质 + MaterialPropertyBlock 逐菜喂色；
        /// 无可映射风味（或 shader 缺失）时回落到普通 Unlit 平涂，与无风味菜表现一致。
        /// </summary>
        private void ApplyFlavorStain()
        {
            if (_spriteRenderer == null)
            {
                return;
            }

            _stainColorScratch.Clear();
            IReadOnlyList<string> flavorIds = Instance?.FlavorIds;
            if (flavorIds != null)
            {
                for (int i = 0; i < flavorIds.Count && _stainColorScratch.Count < MaxStains; i++)
                {
                    if (FlavorStainPalette.TryResolve(flavorIds[i], out Color color)
                        && !_stainColorScratch.Contains(color))
                    {
                        _stainColorScratch.Add(color);
                    }
                }
            }

            if (_stainColorScratch.Count == 0 || SpriteRenderStyle.SpriteStainMaterial == null)
            {
                _spriteRenderer.SetPropertyBlock(null);
                SpriteRenderStyle.ApplyUnlitMaterial(_spriteRenderer);
                return;
            }

            SpriteRenderStyle.ApplyStainMaterial(_spriteRenderer);
            _stainBlock ??= new MaterialPropertyBlock();
            _spriteRenderer.GetPropertyBlock(_stainBlock);
            _stainBlock.SetFloat(StainCountId, _stainColorScratch.Count);
            _stainBlock.SetFloat(StainScaleId, _stainScale);
            _stainBlock.SetFloat(StainThresholdId, _stainThreshold);
            _stainBlock.SetFloat(StainSoftnessId, _stainSoftness);
            _stainBlock.SetFloat(StainDarkenId, _stainDarken);
            _stainBlock.SetFloat(SeedId, Instance != null ? Instance.Id : 0f);
            for (int i = 0; i < MaxStains; i++)
            {
                Color c = i < _stainColorScratch.Count ? _stainColorScratch[i] : Color.clear;
                _stainBlock.SetColor(StainColorIds[i], c);
            }

            _spriteRenderer.SetPropertyBlock(_stainBlock);
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
        /// 阴影随高度变大、变淡、变虚（核心层淡出 + 弥散光晕层显现）。
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

            // 弥散光晕层：大幅放大、贴桌时隐形、高空才显现，做出"越高越虚"的扩散投影。
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
            EnsureSpriteUnderVisualPivot();

            if (_collider == null || _visualPivot == null || _shadowRenderer == null || _spriteRenderer == null || _placementGlow == null)
            {
                Debug.LogError($"{nameof(DishPieceView)} prefab 缺少固定结构：BoxCollider2D/VisualPivot/Sprite/Shadow/PlacementGlow。", this);
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
            if (_collider.OverlapPoint(world))
            {
                _clicked?.Invoke(Instance);
            }
        }

        private void UpdateHover()
        {
            if (!_clickEnabled || Instance == null || _collider == null || WorldInput.PointerOverUi)
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
            SetHovered(false);
        }
    }
}
