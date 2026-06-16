using System;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    public sealed class BoardCellView : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private GridPos _position;
        private Action<GridPos> _clicked;

        public GridPos Position => _position;

        /// <summary>配置一个由 prefab 实例化出来的格子（脚本根 prefab，渲染体在运行时补齐）。</summary>
        public void Configure(
            GridPos position,
            Vector3 worldPosition,
            float size,
            Sprite sprite,
            Action<GridPos> clicked)
        {
            gameObject.name = $"Cell_{position.X}_{position.Y}";
            transform.position = worldPosition;

            // 按 sprite 实际包围盒归一化缩放，使任意导入 PPU 的格图都恰好等于 1 格世界尺寸，
            // 相邻格子边到边对齐、无缝铺满。
            Vector2 bounds = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            float scaleX = bounds.x > 0f ? size / bounds.x : size;
            float scaleY = bounds.y > 0f ? size / bounds.y : size;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            _position = position;
            _clicked = clicked;

            _renderer = gameObject.GetComponent<SpriteRenderer>();
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
            }

            _renderer.sprite = sprite;
            BattleSorting.Apply(_renderer, BattleSorting.Board);
            SpriteRenderStyle.ApplyUnlitMaterial(_renderer);

            // 碰撞体取 sprite 局部包围盒，配合上面的缩放后世界尺寸正好等于 size。
            var boxCollider = gameObject.GetComponent<BoxCollider2D>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider2D>();
            }

            boxCollider.size = bounds.x > 0f && bounds.y > 0f ? bounds : Vector2.one;
        }

        public void SetColor(Color color)
        {
            if (_renderer != null)
            {
                _renderer.color = color;
            }
        }

        private void OnMouseDown()
        {
            _clicked?.Invoke(_position);
        }
    }
}
