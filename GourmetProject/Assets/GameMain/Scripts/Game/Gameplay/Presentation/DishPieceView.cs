using System;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    public sealed class DishPieceView : MonoBehaviour
    {
        // 接触阴影（软边暗斑）参数：贴桌态。偏移按单格尺寸 _cellSize 取比例，适配不同棋盘缩放。
        private const float ShadowBaseAlpha = 0.5f;
        private const float ShadowGroundScale = 1.22f;
        private const float ShadowGroundDrop = 0.16f;
        private const float ShadowGroundSide = 0.06f;

        // 举高态（飞行中）相对贴桌的附加：阴影更远、更大、更淡，模拟悬浮高度。
        private const float ShadowLiftScale = 1.3f;
        private const float ShadowLiftAlphaMul = 0.55f;
        private const float ShadowLiftDrop = 0.24f;
        private const float ShadowLiftSide = 0.12f;

        private Sprite _sprite;
        private float _cellSize;
        private float _pitch;
        private float _lift;
        private SpriteRenderer _shadowRenderer;
        private Vector3 _shadowBaseLocalPos;
        private Vector3 _shadowBaseScale;
        private BoxCollider2D _collider;
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
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            CreateContactShadow(shape);
            CreateFootprintSprite(shape);
            ApplyLift(_lift);

            if (_collider == null)
            {
                _collider = gameObject.AddComponent<BoxCollider2D>();
            }

            _collider.size = new Vector2(
                Mathf.Max(_cellSize, shape.Width * _pitch - (_pitch - _cellSize)),
                Mathf.Max(_cellSize, shape.Height * _pitch - (_pitch - _cellSize)));
            _collider.offset = new Vector2((shape.Width - 1) * _pitch * 0.5f, -(shape.Height - 1) * _pitch * 0.5f);
        }

        /// <summary>脚下软边接触阴影：用径向羽化暗斑铺满整个脚印，不依赖菜品图留白，必定可见。</summary>
        private void CreateContactShadow(DishShape shape)
        {
            var go = new GameObject("Shadow");
            go.transform.SetParent(transform, false);

            Sprite blob = BattleShadow.SoftShadowSprite;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = blob;
            BattleSorting.Apply(renderer, BattleSorting.Pieces, BattleSorting.OrderShadow);
            renderer.color = new Color(0f, 0f, 0f, ShadowBaseAlpha);
            SpriteRenderStyle.ApplyUnlitMaterial(renderer);

            // 阴影覆盖旋转后的实际占格脚印（软边自然探出本体轮廓），无需随朝向旋转。
            float spanX = (shape.Width - 1) * _pitch + _cellSize;
            float spanY = (shape.Height - 1) * _pitch + _cellSize;
            Vector2 bounds = blob != null ? (Vector2)blob.bounds.size : Vector2.one;
            float sx = bounds.x > 0f ? spanX / bounds.x : spanX;
            float sy = bounds.y > 0f ? spanY / bounds.y : spanY;
            _shadowBaseScale = new Vector3(sx * ShadowGroundScale, sy * ShadowGroundScale, 1f);
            go.transform.localScale = _shadowBaseScale;

            Vector3 center = new Vector3(
                (shape.Width - 1) * _pitch * 0.5f,
                -(shape.Height - 1) * _pitch * 0.5f,
                0.05f);
            _shadowBaseLocalPos = center + new Vector3(_cellSize * ShadowGroundSide, -_cellSize * ShadowGroundDrop, 0f);
            go.transform.localPosition = _shadowBaseLocalPos;

            _shadowRenderer = renderer;
        }

        private void CreateFootprintSprite(DishShape shape)
        {
            var go = new GameObject("Sprite");
            go.transform.SetParent(transform, false);

            go.transform.localPosition = new Vector3(
                (shape.Width - 1) * _pitch * 0.5f,
                -(shape.Height - 1) * _pitch * 0.5f,
                0f);

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            BattleSorting.Apply(renderer, BattleSorting.Pieces, BattleSorting.OrderBody);
            renderer.color = Color.white;
            SpriteRenderStyle.ApplyUnlitMaterial(renderer);

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
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            // DishShape.Rotate90 为顺时针；Unity +Z 为逆时针，故顺时针旋转取负角。
            go.transform.localRotation = Quaternion.Euler(0f, 0f, -90f * rot);
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
            t.localPosition = _shadowBaseLocalPos + new Vector3(_cellSize * ShadowLiftSide * lift, -_cellSize * ShadowLiftDrop * lift, 0f);

            float scaleMul = Mathf.Lerp(1f, ShadowLiftScale, lift);
            t.localScale = new Vector3(_shadowBaseScale.x * scaleMul, _shadowBaseScale.y * scaleMul, 1f);

            Color c = _shadowRenderer.color;
            c.a = ShadowBaseAlpha * Mathf.Lerp(1f, ShadowLiftAlphaMul, lift);
            _shadowRenderer.color = c;
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
