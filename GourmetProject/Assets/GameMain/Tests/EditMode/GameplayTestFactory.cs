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
            string flavor = null,
            int rotationIndex = 0)
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
                allowRotate,
                rotationIndex: rotationIndex);
        }

        public static SkillDef Skill(string id, TagEffectType effectType, float effectValue, string termId = "")
        {
            return new SkillDef(id, id, id, effectType, new[] { effectValue }, System.Array.Empty<string>(), termId);
        }

        /// <summary>造一个「前提×行为」规则技能。</summary>
        public static SkillDef RuleSkill(string id, params SkillRuleDef[] rules)
        {
            return new SkillDef(id, id, id, TagEffectType.None, System.Array.Empty<float>(), System.Array.Empty<string>(), string.Empty, rules);
        }

        /// <summary>造一条技能规则，带常用默认值。</summary>
        public static SkillRuleDef Rule(
            SkillActionType actionType,
            float actionValue,
            SkillConditionType condType = SkillConditionType.None,
            SkillScope condScope = SkillScope.Self,
            SkillScope actionScope = SkillScope.Self,
            CountMode condMode = CountMode.Per,
            CountUnit condUnit = CountUnit.Instances,
            CompareOp condCompare = CompareOp.None,
            int condThreshold = 0,
            string condParam = "",
            int actionCount = 0,
            string actionParam = "",
            SkillTrigger trigger = SkillTrigger.OnSettle,
            int order = 0)
        {
            return new SkillRuleDef(
                id: $"r_{actionType}_{order}",
                skillId: string.Empty,
                order: order,
                trigger: trigger,
                condType: condType,
                condScope: condScope,
                condUnit: condUnit,
                condMode: condMode,
                condCompare: condCompare,
                condThreshold: condThreshold,
                condParam: condParam,
                actionType: actionType,
                actionScope: actionScope,
                actionCount: actionCount,
                actionValues: new[] { actionValue },
                actionParams: string.IsNullOrEmpty(actionParam) ? System.Array.Empty<string>() : new[] { actionParam });
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
            DishShape orientation = def.Shape.RotatedBy(rotationIndex);
            var placement = new Placement(orientation, rotationIndex, new GridPos(originX, originY));
            return new DishInstance(id, def, placement, skillIds, flavorId ?? string.Empty);
        }

        public static DishInstance Instance(int id, DishDef def, int originX, int originY, int rotationIndex = 0)
        {
            DishShape orientation = def.Shape.RotatedBy(rotationIndex);
            var placement = new Placement(orientation, rotationIndex, new GridPos(originX, originY));
            return new DishInstance(id, def, placement, def.SkillIds, def.FlavorId);
        }
    }
}
