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
            float pitch)
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
            Vector2 bounds = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            return new Vector3(
                bounds.x > 0f ? spanX / bounds.x : 1f,
                bounds.y > 0f ? spanY / bounds.y : 1f,
                1f);
        }
    }
}
