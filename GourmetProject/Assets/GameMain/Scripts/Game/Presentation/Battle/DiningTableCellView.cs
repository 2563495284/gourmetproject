using System;
using System.Collections.Generic;
using DG.Tweening;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Visual;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 餐桌格子表现：固定结构（渲染体 + 碰撞盒）摆在 prefab 根节点上，由 <see cref="Configure"/> 喂数据。
    /// sprite/位置/尺寸是数据驱动的（随餐桌大小变化），运行时按 sprite 包围盒归一化缩放。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public sealed class DiningTableCellView : MonoBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int PulseAmplitudeId = Shader.PropertyToID("_PulseAmplitude");
        private static readonly int PulseFrequencyId = Shader.PropertyToID("_PulseFrequency");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int BoingId = Shader.PropertyToID("_Boing");
        private static readonly int EdgeClampPointId = Shader.PropertyToID("_EdgeClampPoint");

        [Tooltip("格子渲染体（prefab 根节点上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _renderer;

        [Tooltip("点击命中用碰撞盒（prefab 根节点上的 BoxCollider2D）。")]
        [SerializeField] private BoxCollider2D _collider;

        [Header("Scope 高亮模板")]
        [Tooltip("Scope 高亮层模板。建议在 DiningTableCell prefab 下放一个禁用的 ScopeOutlineTemplate 子物体，设计师可调材质/默认色/排序。")]
        [SerializeField] private SpriteRenderer _scopeOutlinePrefab;
        [Tooltip("Scope 高亮默认材质。留空时使用 GourmetProject/SpriteOutline 的运行时材质。")]
        [SerializeField] private Material _scopeOutlineMaterial;
        [SerializeField, Range(0.25f, 3f)] private float _scopePersistentGlowIntensity = 1.25f;
        [SerializeField, Range(0.25f, 3f)] private float _scopeFlashGlowIntensity = 1.45f;
        [SerializeField, Range(0f, 8f)] private float _scopePersistentPulseSpeed = 0.55f;
        [SerializeField, Range(0f, 8f)] private float _scopeFlashPulseSpeed = 1.8f;
        [SerializeField, Range(0f, 0.5f)] private float _scopePersistentPulseAmplitude = 0.035f;
        [SerializeField, Range(0f, 0.5f)] private float _scopeFlashPulseAmplitude = 0.1f;
        [SerializeField, Range(0f, 64f)] private float _scopePulseFrequency = 18f;

        private GridPos _position;
        private Action<GridPos> _clicked;
        private Action<DiningTableCellView> _hoverEntered;
        private Action<DiningTableCellView> _hoverExited;
        private MaterialPropertyBlock _propertyBlock;
        private readonly Dictionary<int, SpriteRenderer> _scopeOutlineRenderers = new Dictionary<int, SpriteRenderer>();
        private readonly Dictionary<int, MaterialPropertyBlock> _scopeOutlineBlocks = new Dictionary<int, MaterialPropertyBlock>();
        private bool _scopeOutlineTemplateMissingReported;
        private float _configuredSize;
        private bool _hovered;
        private Sequence _transformSequence;

        public GridPos Position => _position;

        public Bounds WorldBounds
        {
            get
            {
                EnsureRefs();
                if (_renderer != null && _renderer.sprite != null)
                {
                    return _renderer.bounds;
                }

                if (_collider != null)
                {
                    return _collider.bounds;
                }

                return new Bounds(transform.position, Vector3.one);
            }
        }

        /// <summary>配置一个由 prefab 实例化出来的格子：结构在 prefab 里摆好，这里只喂数据（局部位置/尺寸/sprite/回调）。</summary>
        public void Configure(
            GridPos position,
            Vector3 localPosition,
            float size,
            Sprite sprite,
            Action<GridPos> clicked)
        {
            EnsureRefs();

            gameObject.name = $"Cell_{position.X}_{position.Y}";
            transform.localPosition = localPosition;

            _position = position;
            _clicked = clicked;

            SetSprite(sprite, size);
        }

        public void SetSprite(Sprite sprite, float size)
        {
            EnsureRefs();
            if (_renderer.sprite == sprite && Mathf.Approximately(_configuredSize, size))
            {
                return;
            }

            // 按 sprite 实际包围盒归一化缩放，使任意导入 PPU 的格图都恰好等于 1 格世界尺寸。
            Vector2 bounds = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            float scaleX = bounds.x > 0f ? size / bounds.x : size;
            float scaleY = bounds.y > 0f ? size / bounds.y : size;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            bool preserveTransformMaterial = IsTransformMaterialActive();
            _renderer.sprite = sprite;
            if (!preserveTransformMaterial)
            {
                _renderer.SetPropertyBlock(null);
                SpriteRenderStyle.ApplyUnlitMaterial(_renderer);
            }

            BattleSorting.Apply(_renderer, BattleSorting.DiningTable);

            // 碰撞体取 sprite 局部包围盒，配合上面的缩放后世界尺寸正好等于 size。
            _collider.size = bounds.x > 0f && bounds.y > 0f ? bounds : Vector2.one;
            _configuredSize = size;
        }

        public void PlayMaterialTransform(Action onSpriteSwitch, Action onComplete)
        {
            EnsureRefs();
            KillTransformSequence(resetMaterial: false);
            if (_renderer == null)
            {
                onSpriteSwitch?.Invoke();
                onComplete?.Invoke();
                return;
            }

            if (SpriteRenderStyle.SpriteTransformMaterial == null)
            {
                _transformSequence = DOTween.Sequence()
                    .Append(transform.DOPunchScale(Vector3.one * 0.08f, 0.24f, vibrato: 6, elasticity: 0.6f))
                    .InsertCallback(0.12f, () => onSpriteSwitch?.Invoke())
                    .OnComplete(() =>
                    {
                        _transformSequence = null;
                        onComplete?.Invoke();
                    });
                return;
            }

            SpriteRenderStyle.ApplyTransformMaterial(_renderer);
            ApplyTransformEffect(0f);
            _transformSequence = DOTween.Sequence()
                .Append(DOTween.To(() => 0f, ApplyTransformEffect, 1f, 0.14f).SetEase(Ease.OutQuad))
                .AppendCallback(() => onSpriteSwitch?.Invoke())
                .Append(DOTween.To(() => 1f, ApplyTransformEffect, 0f, 0.2f).SetEase(Ease.InOutQuad))
                .OnComplete(() =>
                {
                    _transformSequence = null;
                    _renderer.SetPropertyBlock(null);
                    SpriteRenderStyle.ApplyUnlitMaterial(_renderer);
                    onComplete?.Invoke();
                });
        }

        public void SetHoverCallbacks(Action<DiningTableCellView> entered, Action<DiningTableCellView> exited)
        {
            _hoverEntered = entered;
            _hoverExited = exited;
        }

        public void SetColor(Color color)
        {
            if (_renderer != null)
            {
                _renderer.color = color;
            }
        }

        /// <summary>编辑页反馈用：用 shader 画红/绿轮廓，fillAlpha 控制是否保留格子底色。</summary>
        public void SetOutline(Color color, float width, float fillAlpha = 1f)
        {
            EnsureRefs();
            if (SpriteRenderStyle.SpriteOutlineMaterial == null)
            {
                _renderer.color = color;
                return;
            }

            _renderer.color = Color.white;
            SpriteRenderStyle.ApplyOutlineMaterial(_renderer);
            _propertyBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(OutlineColorId, color);
            _propertyBlock.SetFloat(OutlineWidthId, Mathf.Clamp(width, 0f, 0.2f));
            _propertyBlock.SetFloat(FillAlphaId, Mathf.Clamp01(fillAlpha));
            _propertyBlock.SetFloat(GlowIntensityId, 1.05f);
            _propertyBlock.SetFloat(PulseSpeedId, 0f);
            _propertyBlock.SetFloat(PulseAmplitudeId, 0f);
            _propertyBlock.SetFloat(PulseFrequencyId, 18f);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        public void ClearOutline()
        {
            EnsureRefs();
            _renderer.SetPropertyBlock(null);
            SpriteRenderStyle.ApplyUnlitMaterial(_renderer);
        }

        public void SetScopeOutline(BattleScopeHighlightChannel channel, int layer, Color color, float width, float fillAlpha = 0.16f)
        {
            EnsureRefs();
            if (_renderer == null)
            {
                return;
            }

            SpriteRenderer scopeRenderer = EnsureScopeOutlineRenderer(channel, layer);
            if (scopeRenderer == null)
            {
                return;
            }

            scopeRenderer.gameObject.SetActive(true);
            scopeRenderer.sprite = _renderer.sprite;
            scopeRenderer.color = Color.white;
            scopeRenderer.sortingLayerName = _renderer.sortingLayerName;
            scopeRenderer.sortingOrder = _renderer.sortingOrder + 1 + layer;
            ApplyScopeOutlineMaterial(scopeRenderer);

            int key = ScopeLayerKey(channel, layer);
            if (!_scopeOutlineBlocks.TryGetValue(key, out MaterialPropertyBlock block) || block == null)
            {
                block = new MaterialPropertyBlock();
                _scopeOutlineBlocks[key] = block;
            }

            scopeRenderer.GetPropertyBlock(block);
            block.SetColor(OutlineColorId, color);
            block.SetFloat(OutlineWidthId, Mathf.Clamp(width, 0f, 0.2f));
            block.SetFloat(FillAlphaId, Mathf.Clamp01(fillAlpha));
            block.SetFloat(GlowIntensityId, channel == BattleScopeHighlightChannel.Persistent ? _scopePersistentGlowIntensity : _scopeFlashGlowIntensity);
            block.SetFloat(PulseSpeedId, channel == BattleScopeHighlightChannel.Persistent ? _scopePersistentPulseSpeed : _scopeFlashPulseSpeed);
            block.SetFloat(PulseAmplitudeId, channel == BattleScopeHighlightChannel.Persistent ? _scopePersistentPulseAmplitude : _scopeFlashPulseAmplitude);
            block.SetFloat(PulseFrequencyId, _scopePulseFrequency);
            scopeRenderer.SetPropertyBlock(block);
        }

        public void ClearScopeOutlines(BattleScopeHighlightChannel channel)
        {
            foreach (KeyValuePair<int, SpriteRenderer> kv in _scopeOutlineRenderers)
            {
                if (ChannelFromKey(kv.Key) == channel && kv.Value != null)
                {
                    kv.Value.gameObject.SetActive(false);
                }
            }
        }

        public void ClearAllScopeOutlines()
        {
            foreach (SpriteRenderer renderer in _scopeOutlineRenderers.Values)
            {
                if (renderer != null)
                {
                    renderer.gameObject.SetActive(false);
                }
            }
        }

        public void SetDebuffed(bool debuffed)
        {
            EnsureRefs();
            if (_renderer == null)
            {
                return;
            }

            if (debuffed)
            {
                _renderer.SetPropertyBlock(null);
                DebuffVisualStyle.ApplyToSprite(_renderer);
            }
            else if (DebuffVisualStyle.IsAppliedToSprite(_renderer))
            {
                DebuffVisualStyle.ClearSprite(_renderer);
            }
        }

        /// <summary>调整渲染排序序号（编辑页放置预览幽灵需盖在餐桌格之上）。</summary>
        public void SetSortingOrder(int order)
        {
            if (_renderer != null)
            {
                _renderer.sortingOrder = order;
            }
        }

        private void ApplyTransformEffect(float amount)
        {
            if (_renderer == null)
            {
                return;
            }

            float t = Mathf.Clamp01(amount);
            _propertyBlock ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(BrightnessId, t);
            _propertyBlock.SetVector(BoingId, new Vector4(0.2f * t, -0.14f * t, 0f, 0f));
            _propertyBlock.SetVector(EdgeClampPointId, new Vector4(0.24f, 0.24f, 0f, 0f));
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private SpriteRenderer EnsureScopeOutlineRenderer(BattleScopeHighlightChannel channel, int layer)
        {
            int key = ScopeLayerKey(channel, layer);
            if (_scopeOutlineRenderers.TryGetValue(key, out SpriteRenderer existing) && existing != null)
            {
                return existing;
            }

            if (_scopeOutlinePrefab == null)
            {
                if (!_scopeOutlineTemplateMissingReported)
                {
                    Debug.LogError($"{nameof(DiningTableCellView)} prefab 缺少 ScopeOutlineTemplate。", this);
                    _scopeOutlineTemplateMissingReported = true;
                }

                return null;
            }

            Transform parent = _scopeOutlinePrefab.transform.parent != null
                ? _scopeOutlinePrefab.transform.parent
                : transform;
            SpriteRenderer renderer = Instantiate(_scopeOutlinePrefab, parent, false);
            renderer.name = $"ScopeOutline_{channel}_{layer}";
            renderer.gameObject.SetActive(false);
            _scopeOutlineRenderers[key] = renderer;
            return renderer;
        }

        private void ApplyScopeOutlineMaterial(SpriteRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            Material material = _scopeOutlineMaterial != null
                ? _scopeOutlineMaterial
                : SpriteRenderStyle.SpriteOutlineMaterial;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static int ScopeLayerKey(BattleScopeHighlightChannel channel, int layer)
        {
            return ((int)channel * 1000) + Mathf.Clamp(layer, 0, 999);
        }

        private static BattleScopeHighlightChannel ChannelFromKey(int key)
        {
            return key >= 1000 ? BattleScopeHighlightChannel.Flash : BattleScopeHighlightChannel.Persistent;
        }

        private bool IsTransformMaterialActive()
        {
            return _transformSequence != null
                && SpriteRenderStyle.SpriteTransformMaterial != null
                && _renderer != null
                && _renderer.sharedMaterial == SpriteRenderStyle.SpriteTransformMaterial;
        }

        private void KillTransformSequence(bool resetMaterial)
        {
            if (_transformSequence != null)
            {
                _transformSequence.Kill();
                _transformSequence = null;
            }

            if (resetMaterial && _renderer != null)
            {
                _renderer.SetPropertyBlock(null);
                SpriteRenderStyle.ApplyUnlitMaterial(_renderer);
            }
        }

        /// <summary>兜底解析根节点上的渲染体/碰撞盒引用，容忍未在 prefab 里手动赋值的情况。</summary>
        private void EnsureRefs()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
                if (_renderer == null)
                {
                    _renderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }

            if (_collider == null)
            {
                _collider = GetComponent<BoxCollider2D>();
                if (_collider == null)
                {
                    _collider = gameObject.AddComponent<BoxCollider2D>();
                }
            }

            if (_scopeOutlinePrefab == null)
            {
                Transform template = transform.Find("ScopeOutlineTemplate");
                if (template != null)
                {
                    _scopeOutlinePrefab = template.GetComponent<SpriteRenderer>();
                }
            }

            if (_scopeOutlinePrefab != null)
            {
                _scopeOutlinePrefab.gameObject.SetActive(false);
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
            if (_collider == null || WorldInput.PointerOverUi)
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
            KillTransformSequence(resetMaterial: true);
            SetHovered(false);
        }
    }
}
