using System;
using GourmetProject.Gameplay.Model;
using UnityEngine;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>
    /// 棋盘格子表现：固定结构（渲染体 + 碰撞盒）摆在 prefab 根节点上，由 <see cref="Configure"/> 喂数据。
    /// sprite/位置/尺寸是数据驱动的（随棋盘大小变化），运行时按 sprite 包围盒归一化缩放。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public sealed class BoardCellView : MonoBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");

        [Tooltip("格子渲染体（prefab 根节点上的 SpriteRenderer）。")]
        [SerializeField] private SpriteRenderer _renderer;

        [Tooltip("点击命中用碰撞盒（prefab 根节点上的 BoxCollider2D）。")]
        [SerializeField] private BoxCollider2D _collider;

        private GridPos _position;
        private Action<GridPos> _clicked;
        private MaterialPropertyBlock _propertyBlock;

        public GridPos Position => _position;

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

            // 按 sprite 实际包围盒归一化缩放，使任意导入 PPU 的格图都恰好等于 1 格世界尺寸，
            // 相邻格子边到边对齐、无缝铺满。
            Vector2 bounds = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            float scaleX = bounds.x > 0f ? size / bounds.x : size;
            float scaleY = bounds.y > 0f ? size / bounds.y : size;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);

            _position = position;
            _clicked = clicked;

            _renderer.sprite = sprite;
            _renderer.SetPropertyBlock(null);
            BattleSorting.Apply(_renderer, BattleSorting.Board);
            SpriteRenderStyle.ApplyUnlitMaterial(_renderer);

            // 碰撞体取 sprite 局部包围盒，配合上面的缩放后世界尺寸正好等于 size。
            _collider.size = bounds.x > 0f && bounds.y > 0f ? bounds : Vector2.one;
        }

        public void SetColor(Color color)
        {
            if (_renderer != null)
            {
                _renderer.color = color;
            }
        }

        /// <summary>编辑页反馈用：用 shader 画红/绿轮廓，fillAlpha 控制是否保留格子底色。</summary>
        public void SetOutline(Color color, float width, float fillAlpha = 0f)
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
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>调整渲染排序序号（编辑页放置预览幽灵需盖在棋盘格之上）。</summary>
        public void SetSortingOrder(int order)
        {
            if (_renderer != null)
            {
                _renderer.sortingOrder = order;
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
        }

        private void OnMouseDown()
        {
            if (WorldInput.PointerOverUi)
            {
                return;
            }

            _clicked?.Invoke(_position);
        }
    }
}
