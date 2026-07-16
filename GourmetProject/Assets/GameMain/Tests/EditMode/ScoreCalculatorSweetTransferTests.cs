using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ScoreCalculatorSweetTransferTests
    {
        [Test]
        public void OnSettleTransferSkills_ResolvesTargetEffectImmediately()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillRuleDef addFlat = Rule("sweet_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f);
            SkillRuleDef transfer = Rule("sweet_transfer", 1, SkillActionType.TransferSkills, SkillScope.Other, 0f);
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { addFlat, transfer },
                new[] { "美味 +5", "甜蜜传递" });

            DishDef sourceDef = Dish("dish_a", "A", oneCell, "skill_sweet");
            DishDef targetDef = Dish("dish_b", "B", oneCell);
            var db = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { sweetSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(2, 1);
            var source = new DishInstance(1, sourceDef, MakePlacement(oneCell, 0, 0), sourceDef.SkillIds, Array.Empty<string>());
            var target = new DishInstance(2, targetDef, MakePlacement(oneCell, 1, 0), targetDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreLine[] lines = result.ScoreLines.ToArray();

            int sourceAddIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == source.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "甜蜜");
            int transferredAddIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>");
            int targetBaseIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == target.Id
                && l.Kind == ScoreLineKind.DishBase);

            Assert.That(sourceAddIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(transferredAddIndex, Is.GreaterThan(sourceAddIndex));
            Assert.That(targetBaseIndex, Is.GreaterThan(transferredAddIndex));
            Assert.AreEqual(30f, result.RawSum);
            Assert.AreEqual(30, result.Total);
            Assert.AreEqual(1, result.SkillTransfers.Count);
            Assert.AreEqual(target.Id, result.SkillTransfers[0].TargetInstanceId);
        }

        [Test]
        public void OnSettleTransferSkills_OrdersRowTargetsByBoardPosition()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishShape tallShape = DishShape.FromRows(new[] { "X", "X", "X" });
            SkillRuleDef addFlat = Rule("sweet_add", 0, SkillActionType.AddFlat, SkillScope.Self, 5f);
            SkillRuleDef transfer = Rule("sweet_transfer", 1, SkillActionType.TransferSkills, SkillScope.Row, 0f);
            var sweetSkill = new SkillDef(
                "skill_sweet",
                "甜蜜",
                string.Empty,
                Array.Empty<string>(),
                new[] { addFlat, transfer },
                new[] { "美味 +5", "甜蜜传递" });

            DishDef sourceDef = Dish("dish_a", "A", tallShape, "skill_sweet");
            DishDef topTargetDef = Dish("dish_top", "Top", oneCell);
            DishDef bottomTargetDef = Dish("dish_bottom", "Bottom", oneCell);
            var db = new GameplayDatabase(
                new[] { sourceDef, topTargetDef, bottomTargetDef },
                new[] { sweetSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(3, 3);
            var source = new DishInstance(1, sourceDef, MakePlacement(tallShape, 1, 0), sourceDef.SkillIds, Array.Empty<string>());
            var bottomTarget = new DishInstance(2, bottomTargetDef, MakePlacement(oneCell, 0, 2), bottomTargetDef.SkillIds, Array.Empty<string>());
            var topTarget = new DishInstance(3, topTargetDef, MakePlacement(oneCell, 0, 0), topTargetDef.SkillIds, Array.Empty<string>());
            table.Place(source);
            table.Place(bottomTarget);
            table.Place(topTarget);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreLine[] lines = result.ScoreLines.ToArray();

            int topIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == topTarget.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>");
            int bottomIndex = Array.FindIndex(lines, l =>
                l.DishInstanceId == bottomTarget.Id
                && l.Kind == ScoreLineKind.DishFlat
                && l.Source.Name == "A<甜蜜传递>");

            Assert.That(topIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bottomIndex, Is.GreaterThan(topIndex));
        }

        private static SkillRuleDef Rule(
            string id,
            int order,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue)
        {
            return new SkillRuleDef(
                id,
                "skill_sweet",
                order,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                actionType,
                actionScope,
                0,
                new[] { actionValue },
                Array.Empty<string>());
        }

        private static DishDef Dish(string id, string name, DishShape shape, string skillId = null)
        {
            return new DishDef(
                id,
                name,
                10,
                shape,
                0,
                0,
                1f,
                string.IsNullOrEmpty(skillId) ? Array.Empty<string>() : new[] { skillId },
                string.Empty,
                false);
        }

        private static Placement MakePlacement(DishShape shape, int x, int y)
        {
            return new Placement(shape, 0, new GridPos(x, y));
        }
    }
}
