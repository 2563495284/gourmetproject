using System;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Gameplay.Presentation
{
    public sealed class DishPieceView : MonoBehaviour
    {
        private Sprite _sprite;
        private float _cellSize;
        private float _pitch;
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

        private void RebuildCells(DishShape shape)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            CreateFootprintSprite(shape, shadow: true);
            CreateFootprintSprite(shape, shadow: false);

            if (_collider == null)
            {
                _collider = gameObject.AddComponent<BoxCollider2D>();
            }

            _collider.size = new Vector2(
                Mathf.Max(_cellSize, shape.Width * _pitch - (_pitch - _cellSize)),
                Mathf.Max(_cellSize, shape.Height * _pitch - (_pitch - _cellSize)));
            _collider.offset = new Vector2((shape.Width - 1) * _pitch * 0.5f, -(shape.Height - 1) * _pitch * 0.5f);
        }

        private void CreateFootprintSprite(DishShape shape, bool shadow)
        {
            var go = new GameObject(shadow ? "Shadow" : "Sprite");
            go.transform.SetParent(transform, false);

            Vector3 offset = new Vector3(
                (shape.Width - 1) * _pitch * 0.5f,
                -(shape.Height - 1) * _pitch * 0.5f,
                shadow ? 0.03f : 0f);
            if (shadow)
            {
                offset += new Vector3(0.08f, -0.08f, 0f);
            }

            go.transform.localPosition = offset;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.sortingOrder = shadow ? 35 : 40;
            renderer.color = shadow ? new Color(0f, 0f, 0f, 0.28f) : Color.white;
            if (!shadow)
            {
                SpriteRenderStyle.ApplyLitMaterial(renderer);
            }

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
