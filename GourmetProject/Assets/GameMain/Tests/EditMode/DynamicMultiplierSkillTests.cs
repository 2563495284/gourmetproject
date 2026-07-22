using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class DynamicMultiplierSkillTests
    {
        [Test]
        public void MangoSago_AddsAllOneCellCurrentMultipliersToSelf()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishShape twoCells = DishShape.FromRows(new[] { "XX" });
            DishShape mangoShape = DishShape.FromRows(new[] { "XXX" });
            SkillDef mangoSkill = Skill(
                "sk_mango_sago",
                Rule(
                    "sk_mango_sago_2",
                    "sk_mango_sago",
                    0,
                    SkillActionType.AddCurrentMult,
                    SkillScope.Self,
                    "source:one-cell"));
            DishDef firstDef = Dish("one_a", oneCell);
            DishDef secondDef = Dish("one_b", oneCell);
            DishDef ignoredDef = Dish("two", twoCells);
            DishDef mangoDef = Dish("mango_sago", mangoShape, "sk_mango_sago");
            GameplayDatabase db = Database(new[] { firstDef, secondDef, ignoredDef, mangoDef }, mangoSkill);
            var table = new DiningTable(7, 1);
            DishInstance first = Instance(1, firstDef, oneCell, 0);
            DishInstance second = Instance(2, secondDef, oneCell, 1);
            DishInstance ignored = Instance(3, ignoredDef, twoCells, 2);
            DishInstance mango = Instance(4, mangoDef, mangoShape, 4);
            first.AddServeMultiplierFlat(2f);      // 当前倍率 3
            second.AddServeMultiplierFlat(0.5f);  // 当前倍率 1.5
            ignored.AddServeMultiplierFlat(10f);  // 两格食物，不应计入
            Place(table, first, second, ignored, mango);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(ScoreOf(result, mango).Multiplier, Is.EqualTo(5.5f).Within(0.0001f));
        }

        [Test]
        public void Chiffon_AddsItsSnapshottedMultiplierToEveryCake()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef chiffonSkill = Skill(
                "sk_chiffon",
                Rule("sk_chiffon_1", "sk_chiffon", 0, SkillActionType.AddMultFlat, SkillScope.Self, value: 8f),
                Rule("sk_chiffon_2", "sk_chiffon", 1, SkillActionType.AddCurrentMult, SkillScope.Category, "cat:cake"));
            DishDef chiffonDef = Dish("chiffion", cell, "sk_chiffon", "cake");
            DishDef cakeDef = Dish("cake", cell, category: "cake");
            DishDef otherDef = Dish("other", cell);
            GameplayDatabase db = Database(new[] { chiffonDef, cakeDef, otherDef }, chiffonSkill);
            var table = new DiningTable(3, 1);
            DishInstance chiffon = Instance(1, chiffonDef, cell, 0);
            DishInstance cake = Instance(2, cakeDef, cell, 1);
            DishInstance other = Instance(3, otherDef, cell, 2);
            Place(table, chiffon, cake, other);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(ScoreOf(result, chiffon).Multiplier, Is.EqualTo(18f).Within(0.0001f));
            Assert.That(ScoreOf(result, cake).Multiplier, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(ScoreOf(result, other).Multiplier, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void CreamCake_AddsItsSnapshottedCurrentScoreToEveryCake()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef creamCakeSkill = Skill(
                "sk_cream_cake",
                Rule("sk_cream_cake_1", "sk_cream_cake", 0, SkillActionType.PermanentAddFlat, SkillScope.Self, value: 10f),
                Rule("sk_cream_cake_2", "sk_cream_cake", 1, SkillActionType.AddCurrentScore, SkillScope.Category, "cat:cake"));
            DishDef creamCakeDef = Dish("cream_cake", cell, "sk_cream_cake", "cake");
            DishDef cakeDef = Dish("cake", cell, category: "cake");
            DishDef otherDef = Dish("other", cell);
            GameplayDatabase db = Database(new[] { creamCakeDef, cakeDef, otherDef }, creamCakeSkill);
            var table = new DiningTable(3, 1);
            DishInstance creamCake = Instance(1, creamCakeDef, cell, 0);
            DishInstance cake = Instance(2, cakeDef, cell, 1);
            DishInstance other = Instance(3, otherDef, cell, 2);
            Place(table, creamCake, cake, other);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            // 基础分 10 + 一技能永久加分 10 = 快照分数 20；二技能把该快照加给所有蛋糕。
            Assert.That(ScoreOf(result, creamCake).FlatBonus, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(ScoreOf(result, cake).FlatBonus, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(ScoreOf(result, other).FlatBonus, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Palmier_MultipliesRoundAndSelfByLinearColumnDishCount()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef palmierSkill = Skill(
                "sk_palmier",
                Rule("sk_palmier_1", "sk_palmier", 0, SkillActionType.AddCountAs, SkillScope.Self, value: 3f),
                Rule(
                    "sk_palmier_2",
                    "sk_palmier",
                    1,
                    SkillActionType.AddMult,
                    SkillScope.RoundAndSelf,
                    "linear",
                    0.05f,
                    SkillConditionType.DishCount,
                    SkillScope.ColumnAndSelf));
            DishDef palmierDef = Dish("palmier", cell, "sk_palmier");
            DishDef otherDef = Dish("other", cell);
            GameplayDatabase db = Database(new[] { palmierDef, otherDef }, palmierSkill);
            var table = new DiningTable(4, 2);
            DishInstance palmier = Instance(1, palmierDef, cell, 1, 1);
            DishInstance sameColumn = Instance(2, otherDef, cell, 1, 0);
            DishInstance adjacent = Instance(3, otherDef, cell, 2, 1);
            DishInstance distant = Instance(4, otherDef, cell, 3, 0);
            Place(table, palmier, sameColumn, adjacent, distant);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            // 本列食物数 = 蝴蝶酥视为 4 个 + 同列食物 1 个，因此 N = 1 + 5×0.05 = 1.25。
            Assert.That(ScoreOf(result, palmier).Multiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(ScoreOf(result, sameColumn).Multiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(ScoreOf(result, adjacent).Multiplier, Is.EqualTo(1.25f).Within(0.0001f));
            Assert.That(ScoreOf(result, distant).Multiplier, Is.EqualTo(1f).Within(0.0001f));
        }

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);

        private static SkillDef Skill(string id, params SkillRuleDef[] rules)
        {
            return new SkillDef(
                id,
                id,
                string.Empty,
                Array.Empty<string>(),
                rules,
                rules.Select(rule => rule.Id).ToArray());
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            int order,
            SkillActionType actionType,
            SkillScope actionScope,
            string actionParam = null,
            float value = 0f,
            SkillConditionType condType = SkillConditionType.None,
            SkillScope condScope = SkillScope.Self)
        {
            return new SkillRuleDef(
                id,
                skillId,
                order,
                SkillTrigger.OnSettle,
                condType,
                condScope,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                actionType,
                actionScope,
                0,
                new[] { value },
                string.IsNullOrEmpty(actionParam) ? Array.Empty<string>() : new[] { actionParam });
        }

        private static DishDef Dish(
            string id,
            DishShape shape,
            string skillId = null,
            string category = null)
        {
            return new DishDef(
                id,
                id,
                10,
                shape,
                0,
                0,
                1f,
                string.IsNullOrEmpty(skillId) ? Array.Empty<string>() : new[] { skillId },
                string.Empty,
                false,
                category: category);
        }

        private static DishInstance Instance(int id, DishDef def, DishShape shape, int x, int y = 0)
        {
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                def.SkillIds,
                Array.Empty<string>());
        }

        private static GameplayDatabase Database(DishDef[] dishes, params SkillDef[] skills)
        {
            return new GameplayDatabase(
                dishes,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private static void Place(DiningTable table, params DishInstance[] dishes)
        {
            foreach (DishInstance dish in dishes)
            {
                table.Place(dish);
            }
        }
    }
}
