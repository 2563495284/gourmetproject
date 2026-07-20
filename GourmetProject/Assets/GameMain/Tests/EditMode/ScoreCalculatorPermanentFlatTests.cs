using System;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ScoreCalculatorPermanentFlatTests
    {
        [Test]
        public void RecipeFlatBonusIsBaseScoreAndPermanentSkillAddsOnlyCurrentValue()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillDef permSkill = Skill(
                "skill_perm",
                "永久加分",
                Rule("perm", "skill_perm", SkillActionType.PermanentAddFlat, SkillScope.Self, 6f));
            DishDef dishDef = Dish("dish_egg_tart", "蛋挞", oneCell, "skill_perm");
            var db = new GameplayDatabase(
                new[] { dishDef },
                new[] { permSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(1, 1);
            var entry = new RecipeSlotEntry(
                dishDef.Id,
                null,
                null,
                scoreMultiplier: 1f,
                scoreFlatBonus: 6f,
                sourceBookIndex: 0,
                sourceDishIndex: 0);
            var session = new BattleSession(
                table,
                db,
                new Xoshiro256SS(0x12345678UL),
                new[] { new RecipeSlot("菜谱1", new[] { entry }) },
                requiredScore: 0);

            ServeResult serve = session.Serve(0);
            Assert.That(serve.Success, Is.True);
            Assert.AreEqual(12f, serve.Dish.BaseScoreBeforeSettlement);

            ScoreResult result = session.Settle();

            Assert.AreEqual(18, result.Total);
            Assert.That(result.ScoreLines.Any(l =>
                l.DishInstanceId == serve.Dish.Id
                && l.Kind == ScoreLineKind.DishBase
                && Math.Abs(l.Value - 12f) < 0.001f), Is.True);
            Assert.That(result.ScoreLines.Any(l =>
                l.DishInstanceId == serve.Dish.Id
                && l.Kind == ScoreLineKind.DishFlat
                && Math.Abs(l.Value - 6f) < 0.001f), Is.True);
            Assert.That(session.LastRecipeScoreFlatDeltas.Count, Is.EqualTo(1));
            Assert.AreEqual(0, session.LastRecipeScoreFlatDeltas[0].BookIndex);
            Assert.AreEqual(0, session.LastRecipeScoreFlatDeltas[0].DishIndex);
            Assert.AreEqual(6f, session.LastRecipeScoreFlatDeltas[0].Delta);
        }

        [Test]
        public void RecipeMultiplierBonusIsInitialMultiplierAndPermanentSkillMultipliesOnlyCurrentValue()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            SkillDef permSkill = Skill(
                "skill_perm_mult",
                "永久乘区",
                Rule("perm_mult", "skill_perm_mult", SkillActionType.PermanentAddMult, SkillScope.Self, 3f));
            DishDef dishDef = Dish("dish_mult", "倍率菜", oneCell, "skill_perm_mult");
            var db = new GameplayDatabase(
                new[] { dishDef },
                new[] { permSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(1, 1);
            var entry = new RecipeSlotEntry(
                dishDef.Id,
                null,
                null,
                scoreMultiplier: 2f,
                scoreFlatBonus: 0f,
                sourceBookIndex: 0,
                sourceDishIndex: 0);
            var session = new BattleSession(
                table,
                db,
                new Xoshiro256SS(0x87654321UL),
                new[] { new RecipeSlot("菜谱1", new[] { entry }) },
                requiredScore: 0);

            ServeResult serve = session.Serve(0);
            Assert.That(serve.Success, Is.True);
            Assert.AreEqual(2f, serve.Dish.BaseMultiplierBeforeSettlement);

            ScoreResult result = session.Settle();

            Assert.AreEqual(36, result.Total);
            Assert.That(result.ScoreLines.Any(l =>
                l.DishInstanceId == serve.Dish.Id
                && l.Kind == ScoreLineKind.DishMultiplier
                && Math.Abs(l.Value - 3f) < 0.001f), Is.True);
            Assert.That(session.LastRecipeScoreMultiplierDeltas.Count, Is.EqualTo(1));
            Assert.AreEqual(0, session.LastRecipeScoreMultiplierDeltas[0].BookIndex);
            Assert.AreEqual(0, session.LastRecipeScoreMultiplierDeltas[0].DishIndex);
            Assert.AreEqual(3f, session.LastRecipeScoreMultiplierDeltas[0].Multiplier);
        }

        [Test]
        public void DishContributionIsCeiledBeforeRawSum()
        {
            DishShape oneCell = DishShape.FromRows(new[] { "X" });
            DishDef dishDef = new DishDef(
                "dish_fractional",
                "小数菜",
                10,
                oneCell,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
            var db = new GameplayDatabase(
                new[] { dishDef },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(1, 1);
            var dish = new DishInstance(
                1,
                dishDef,
                new Placement(oneCell, 0, new GridPos(0, 0)),
                dishDef.SkillIds,
                Array.Empty<string>());
            dish.MultiplyServeMultiplier(1.01f);
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.DishScores, Has.Count.EqualTo(1));
            Assert.AreEqual(11f, result.DishScores.Single().Contribution);
            Assert.AreEqual(11f, result.RawSum);
            Assert.AreEqual(11, result.Total);
        }

        private static SkillDef Skill(string id, string name, params SkillRuleDef[] rules)
        {
            return new SkillDef(id, name, string.Empty, Array.Empty<string>(), rules, rules.Select(_ => string.Empty).ToArray());
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            SkillActionType actionType,
            SkillScope actionScope,
            float actionValue)
        {
            return new SkillRuleDef(
                id,
                skillId,
                0,
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

        private static DishDef Dish(string id, string name, DishShape shape, string skillId)
        {
            return new DishDef(
                id,
                name,
                6,
                shape,
                0,
                0,
                1f,
                new[] { skillId },
                string.Empty,
                false);
        }
    }
}
