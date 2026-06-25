using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Tests
{
    /// <summary>玩法单测的轻量构造助手：快速造出菜品定义与棋盘实例。</summary>
    internal static class GameplayTestFactory
    {
        public static DishDef Dish(
            string id,
            string[] rows,
            int deliciousness = 10,
            int? initScore = null,
            int hiddenMin = 0,
            int hiddenMax = 100,
            float baseWeight = 100f,
            bool allowRotate = true,
            IReadOnlyList<string> tags = null)
        {
            return new DishDef(
                id,
                id,
                deliciousness,
                initScore ?? deliciousness,
                DishShape.FromRows(rows),
                hiddenMin,
                hiddenMax,
                baseWeight,
                tags ?? new List<string>(),
                string.Empty,
                allowRotate);
        }

        public static TagDef Tag(
            string id,
            TagEffectType effectType,
            float effectValue,
            TagCategory category = TagCategory.Inherent,
            string termId = "")
        {
            return new TagDef(id, id, id, category, effectType, new[] { effectValue }, System.Array.Empty<string>(), termId);
        }

        public static DishInstance InstanceWithTags(int id, DishDef def, int originX, int originY, IReadOnlyList<string> tagIds, int rotationIndex = 0)
        {
            IReadOnlyList<DishShape> orientations = def.Shape.GetOrientations(def.AllowRotate);
            DishShape orientation = orientations[rotationIndex];
            var placement = new Placement(orientation, rotationIndex, new GridPos(originX, originY));
            return new DishInstance(id, def, placement, tagIds);
        }

        public static DishInstance Instance(int id, DishDef def, int originX, int originY, int rotationIndex = 0)
        {
            IReadOnlyList<DishShape> orientations = def.Shape.GetOrientations(def.AllowRotate);
            DishShape orientation = orientations[rotationIndex];
            var placement = new Placement(orientation, rotationIndex, new GridPos(originX, originY));
            return new DishInstance(id, def, placement, def.InherentTags);
        }
    }
}
