using System;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    /// <summary>
    /// 已摆放菜品表现：固定结构（接触阴影 + 菜品本体 + 碰撞盒）预拼在 prefab 上，由 <see cref="BuildPlaced"/> 喂数据。
    /// sprite/缩放/旋转/碰撞尺寸随形状(1x1/2x1/L/T...)与朝向变化，必须运行时计算（见 dish-footprint-sprite 规则）。
    /// 阴影一律走假阴影软暗斑（见 battle-fake-shadow 规则），全程 Unlit 平涂，不依赖 Light2D。
    /// </summary>
    public sealed class DishPieceView : MonoBehaviour
    {
        [Header("接触阴影：贴桌态（偏移按单格尺寸取比例，适配不同棋盘缩放）")]
        [SerializeField] private float _shadowBaseAlpha = 0.5f;
        [SerializeField] private float _shadowGroundScale = 1.22f;
        [SerializeField] private float _shadowGroundDrop = 0.16f;
        [SerializeField] private float _shadowGroundSide = 0.06f;

        [Header("接触阴影：举高态（飞行中）相对贴桌的附加（更远/更大/更淡）")]
        [SerializeField] private float _shadowLiftScale = 1.3f;
        [SerializeField] private float _shadowLiftAlphaMul = 0.55f;
        [SerializeField] private float _shadowLiftDrop = 0.24f;
        [SerializeField] private float _shadowLiftSide = 0.12f;

        [Header("固定结构（prefab 预拼，运行时引用）")]
        [Tooltip("菜品本体渲染体（子物体 Sprite 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [Tooltip("脚下接触阴影（子物体 Shadow 上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _shadowRenderer;
        [Tooltip("点击命中碰撞盒（prefab 根节点上的 BoxCollider2D）。")]
        [SerializeField] private BoxCollider2D _collider;

        private Sprite _sprite;
        private float _cellSize;
        private float _pitch;
        private float _lift;
        private Vector3 _shadowBaseLocalPos;
        private Vector3 _shadowBaseScale;
        private Action<DishInstance> _clicked;

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

        public void SetGhost(bool ghost)
        {
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
            {
                Color color = renderer.color;
                color.a = ghost ? Mathf.Min(color.a, 0.65f) : Mathf.Max(color.a, 0.95f);
                renderer.color = color;
            }
        }

        /// <summary>飞入棋盘时切到 PiecesFlying 层，整体压在已摆放食品之上；落定后切回 Pieces 层。</summary>
        public void SetFlying(bool flying)
        {
            string layer = flying ? BattleSorting.PiecesFlying : BattleSorting.Pieces;
            foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
            {
                renderer.sortingLayerName = layer;
            }
        }

        private void RebuildCells(DishShape shape)
        {
            EnsureRefs();

            ConfigureContactShadow(shape);
            ConfigureFootprintSprite(shape);
            ApplyLift(_lift);

            _collider.size = new Vector2(
                Mathf.Max(_cellSize, shape.Width * _pitch - (_pitch - _cellSize)),
                Mathf.Max(_cellSize, shape.Height * _pitch - (_pitch - _cellSize)));
            _collider.offset = new Vector2((shape.Width - 1) * _pitch * 0.5f, -(shape.Height - 1) * _pitch * 0.5f);
        }

        /// <summary>脚下软边接触阴影：用径向羽化暗斑铺满整个脚印，不依赖菜品图留白，必定可见。</summary>
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
        }

        private void ConfigureFootprintSprite(DishShape shape)
        {
            Transform t = _spriteRenderer.transform;
            t.localPosition = new Vector3(
                (shape.Width - 1) * _pitch * 0.5f,
                -(shape.Height - 1) * _pitch * 0.5f,
                0f);

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
        }

        /// <summary>设置悬浮高度：0=贴桌，1=举高（飞行中）。阴影随高度变远、变大、变淡，模拟接触投影的高度感。</summary>
        public void SetLift(float lift01)
        {
            _lift = Mathf.Clamp01(lift01);
            ApplyLift(_lift);
        }

        private void ApplyLift(float lift)
        {
            if (_shadowRenderer == null)
            {
                return;
            }

            Transform t = _shadowRenderer.transform;
            t.localPosition = _shadowBaseLocalPos + new Vector3(_cellSize * _shadowLiftSide * lift, -_cellSize * _shadowLiftDrop * lift, 0f);

            float scaleMul = Mathf.Lerp(1f, _shadowLiftScale, lift);
            t.localScale = new Vector3(_shadowBaseScale.x * scaleMul, _shadowBaseScale.y * scaleMul, 1f);

            Color c = _shadowRenderer.color;
            c.a = _shadowBaseAlpha * Mathf.Lerp(1f, _shadowLiftAlphaMul, lift);
            _shadowRenderer.color = c;
        }

        /// <summary>兜底解析/补齐 prefab 预拼的渲染体与碰撞盒，容忍未在 prefab 里手动赋值的情况。</summary>
        private void EnsureRefs()
        {
            if (_collider == null)
            {
                _collider = GetComponent<BoxCollider2D>();
                if (_collider == null)
                {
                    _collider = gameObject.AddComponent<BoxCollider2D>();
                }
            }

            _shadowRenderer = ResolveChildRenderer(_shadowRenderer, "Shadow");
            _spriteRenderer = ResolveChildRenderer(_spriteRenderer, "Sprite");
        }

        private SpriteRenderer ResolveChildRenderer(SpriteRenderer current, string childName)
        {
            if (current != null)
            {
                return current;
            }

            Transform t = transform.Find(childName);
            if (t == null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(transform, false);
                t = go.transform;
            }

            SpriteRenderer renderer = t.GetComponent<SpriteRenderer>();
            return renderer != null ? renderer : t.gameObject.AddComponent<SpriteRenderer>();
        }

        private void Update()
        {
            if (Instance == null || !WorldInput.PrimaryPressedThisFrame || _collider == null)
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
    }
}
