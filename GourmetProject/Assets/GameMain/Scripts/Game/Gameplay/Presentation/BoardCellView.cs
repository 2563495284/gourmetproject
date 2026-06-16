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

        public static BoardCellView Create(
            Transform parent,
            GridPos position,
            Vector3 worldPosition,
            float size,
            Sprite sprite,
            int sortingOrder,
            Action<GridPos> clicked)
        {
            var go = new GameObject($"Cell_{position.X}_{position.Y}");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPosition;

            // 按 sprite 实际包围盒归一化缩放，使任意导入 PPU 的格图都恰好等于 1 格世界尺寸，
            // 相邻格子边到边对齐、无缝铺满。
            Vector2 bounds = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            float scaleX = bounds.x > 0f ? size / bounds.x : size;
            float scaleY = bounds.y > 0f ? size / bounds.y : size;
            go.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            var view = go.AddComponent<BoardCellView>();
            view._position = position;
            view._clicked = clicked;
            view._renderer = go.AddComponent<SpriteRenderer>();
            view._renderer.sprite = sprite;
            view._renderer.sortingOrder = sortingOrder;
            SpriteRenderStyle.ApplyLitMaterial(view._renderer);

            // 碰撞体取 sprite 局部包围盒，配合上面的缩放后世界尺寸正好等于 size。
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = bounds.x > 0f && bounds.y > 0f ? bounds : Vector2.one;
            return view;
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
