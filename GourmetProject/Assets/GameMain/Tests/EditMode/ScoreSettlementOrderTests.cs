using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ScoreSettlementOrderTests
    {
        [Test]
        public void SameOriginUsesFirstActuallyOccupiedCellInsteadOfInstanceId()
        {
            DishShape hollowCorner = DishShape.FromRows(new[] { ".X", "XX" });
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishDef palmierDef = Dish("palmier", "蝴蝶酥", hollowCorner);
            DishDef eggTartDef = Dish("eggtart", "蛋挞", oneCell);
            var db = Database(palmierDef, eggTartDef);
            var table = new DiningTable(3, 2);
            var palmier = new DishInstance(
                1,
                palmierDef,
                new Placement(hollowCorner, 0, new GridPos(0, 0)),
                palmierDef.SkillIds,
                Array.Empty<string>());
            var eggTart = new DishInstance(
                8,
                eggTartDef,
                new Placement(oneCell, 0, new GridPos(0, 0)),
                eggTartDef.SkillIds,
                Array.Empty<string>());
            table.Place(palmier);
            table.Place(eggTart);

            var snapshot = new ScoreSnapshot(table, db);
            var reversed = new ScoreSnapshot(table, db, reverseDishOrder: true);

            Assert.That(snapshot.DishesInDefaultOrder.Select(d => d.Id), Is.EqualTo(new[] { 8, 1 }));
            Assert.That(reversed.DishesInDefaultOrder.Select(d => d.Id), Is.EqualTo(new[] { 1, 8 }));
        }

        [Test]
        public void PerZeroEmptyCellSkillEmitsExecutedZeroScoreLine()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            var rule = new SkillRuleDef(
                "sk_pudding#0",
                "sk_pudding",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.EmptyCell,
                SkillScope.Round,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                new[] { -2f },
                Array.Empty<string>());
            var skill = new SkillDef(
                "sk_pudding",
                "布丁",
                string.Empty,
                Array.Empty<string>(),
                new[] { rule },
                new[] { string.Empty });
            DishDef puddingDef = new DishDef(
                "pudding",
                "布丁",
                10,
                oneCell,
                0,
                0,
                1f,
                new[] { skill.Id },
                string.Empty,
                false);
            var db = new GameplayDatabase(
                new[] { puddingDef },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(1, 1);
            var pudding = new DishInstance(
                1,
                puddingDef,
                new Placement(oneCell, 0, new GridPos(0, 0)),
                puddingDef.SkillIds,
                Array.Empty<string>());
            table.Place(pudding);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreLine line = result.ScoreLines.Single(l =>
                l.Kind == ScoreLineKind.DishFlat
                && l.Source?.Type == ScoreSourceType.DishSkill);

            Assert.That(line.Value, Is.EqualTo(0f));
            Assert.That(line.Trace, Is.Not.Null);
            Assert.That(result.ScoreEvents.Any(e =>
                e.Type == ScoreEventType.CommandExecuted
                && e.Trace != null
                && e.Trace.RuleId == rule.Id), Is.True);
        }

        private static GameplayDatabase Database(params DishDef[] dishes)
        {
            return new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private static DishDef Dish(string id, string name, DishShape shape)
        {
            return new DishDef(
                id,
                name,
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
        }
    }
}
