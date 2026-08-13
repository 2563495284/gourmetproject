using GourmetProject.Gameplay.Model;
using UnityEngine;

namespace GourmetProject.Game.Presentation.Battle
{
    /// <summary>战斗本体与 RT 预览共用的食物占格几何，避免两套缩放规则逐渐偏离。</summary>
    internal static class DishVisualLayout
    {
        public static Vector2 FootprintSpan(
            DishShape shape,
            float cellSize,
            float pitch)
        {
            if (shape == null)
            {
                return Vector2.one * Mathf.Max(0.0001f, cellSize);
            }

            return new Vector2(
                (shape.Width - 1) * pitch + cellSize,
                (shape.Height - 1) * pitch + cellSize);
        }

        /// <summary>
        /// Sprite 始终按基础朝向缩放，再由调用方按 rotationIndex 旋转；
        /// displayShape 是已经旋转后的实际占格形状。
        /// </summary>
        public static Vector3 SpriteScale(
            Sprite sprite,
            DishShape displayShape,
            int rotationIndex,
            float cellSize,
            float pitch,
            bool useTightMeshBounds = false)
        {
            int rot = ((rotationIndex % 4) + 4) % 4;
            bool swapped = (rot % 2) == 1;
            int baseWidth = displayShape != null
                ? (swapped ? displayShape.Height : displayShape.Width)
                : 1;
            int baseHeight = displayShape != null
                ? (swapped ? displayShape.Width : displayShape.Height)
                : 1;
            float spanX = (baseWidth - 1) * pitch + cellSize;
            float spanY = (baseHeight - 1) * pitch + cellSize;
            Vector2 bounds = useTightMeshBounds
                ? (Vector2)SpriteMeshBounds(sprite).size
                : sprite != null
                    ? (Vector2)sprite.bounds.size
                    : Vector2.one;
            return new Vector3(
                bounds.x > 0f ? spanX / bounds.x : 1f,
                bounds.y > 0f ? spanY / bounds.y : 1f,
                1f);
        }

        /// <summary>
        /// Sprite.bounds 是完整导入矩形，会把 PNG 的透明留白也算进缩放。
        /// Tight mesh 顶点包围盒更接近真正可见的食物轮廓，供无棋盘底图的仓库预览使用。
        /// </summary>
        public static Bounds SpriteMeshBounds(Sprite sprite)
        {
            if (sprite == null)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Vector2[] vertices = sprite.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return sprite.bounds;
            }

            Vector2 min = vertices[0];
            Vector2 max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector2.Min(min, vertices[i]);
                max = Vector2.Max(max, vertices[i]);
            }

            Vector2 size = max - min;
            if (size.x <= 0.0001f || size.y <= 0.0001f)
            {
                return sprite.bounds;
            }

            return new Bounds(
                (min + max) * 0.5f,
                size);
        }

        public static Vector3 PositionToCenterSpriteBounds(
            Bounds spriteBounds,
            Vector3 scale,
            Quaternion rotation)
        {
            Vector3 scaledCenter = Vector3.Scale(spriteBounds.center, scale);
            return -(rotation * scaledCenter);
        }
    }
}
