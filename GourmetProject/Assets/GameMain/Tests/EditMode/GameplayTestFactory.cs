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
            int hiddenMin = 0,
            int hiddenMax = 100,
            float baseWeight = 100f,
            bool allowRotate = true,
            IReadOnlyList<string> skills = null,
            string flavor = null)
        {
            return new DishDef(
                id,
                id,
                deliciousness,
                DishShape.FromRows(rows),
                hiddenMin,
                hiddenMax,
                baseWeight,
                skills ?? new List<string>(),
                flavor ?? string.Empty,
                string.Empty,
                allowRotate);
        }

        public static SkillDef Skill(string id, TagEffectType effectType, float effectValue, string termId = "")
        {
            return new SkillDef(id, id, id, effectType, new[] { effectValue }, System.Array.Empty<string>(), termId);
        }

        public static FlavorDef Flavor(string id, TagEffectType effectType, float effectValue, string termId = "")
        {
            return new FlavorDef(id, id, id, effectType, new[] { effectValue }, System.Array.Empty<string>(), termId);
        }

        public static CellTagDef CellTag(string id, TagEffectType effectType, float effectValue, string termId = "")
        {
            return new CellTagDef(id, id, id, effectType, new[] { effectValue }, System.Array.Empty<string>(), termId);
        }

        /// <summary>造带指定技能（与可选风味）的棋盘菜品实例。第 5 参数即技能 id 列表。</summary>
        public static DishInstance InstanceWithTags(int id, DishDef def, int originX, int originY, IReadOnlyList<string> skillIds, string flavorId = null, int rotationIndex = 0)
        {
            IReadOnlyList<DishShape> orientations = def.Shape.GetOrientations(def.AllowRotate);
            DishShape orientation = orientations[rotationIndex];
            var placement = new Placement(orientation, rotationIndex, new GridPos(originX, originY));
            return new DishInstance(id, def, placement, skillIds, flavorId ?? string.Empty);
        }

        public static DishInstance Instance(int id, DishDef def, int originX, int originY, int rotationIndex = 0)
        {
            IReadOnlyList<DishShape> orientations = def.Shape.GetOrientations(def.AllowRotate);
            DishShape orientation = orientations[rotationIndex];
            var placement = new Placement(orientation, rotationIndex, new GridPos(originX, originY));
            return new DishInstance(id, def, placement, def.SkillIds, def.FlavorId);
        }
    }
}
