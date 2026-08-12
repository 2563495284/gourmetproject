using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ItemScoreEffectSourceTests
    {
        [Test]
        public void PerSkillMultiplier_UsesEachDishOwnSkillCount()
        {
            var table = new DiningTable(4, 1);
            DishInstance twoSkills = CreateDish(1, "two", 10, 0, "skill_a", "skill_b");
            DishInstance oneSkill = CreateDish(2, "one", 20, 1, "skill_c");
            DishInstance noSkills = CreateDish(3, "none", 30, 2);
            DishInstance excluded = CreateDish(4, "excluded", 40, 3,
                "skill_d", "skill_e", "skill_f", "skill_g");
            excluded.ExcludeFromScore();

            table.Place(twoSkills);
            table.Place(oneSkill);
            table.Place(noSkills);
            table.Place(excluded);

            var spec = new ItemScoreSpec(
                ItemScoreEffectType.PerSkillMultFlat,
                0.2f,
                string.Empty,
                "item_skill_count_mult",
                "主厨刀架");
            var calculator = new ScoreCalculator(
                effectSources: new[] { new ItemScoreEffectSource(new[] { spec }) });

            ScoreResult result = calculator.Calculate(table, CreateEmptyDatabase());

            Assert.That(result.DishScores, Has.Count.EqualTo(3));
            Assert.That(result.DishScores.Single(x => x.DishId == "two").Multiplier.ToDouble(),
                Is.EqualTo(1.4d).Within(0.0001d));
            Assert.That(result.DishScores.Single(x => x.DishId == "one").Multiplier.ToDouble(),
                Is.EqualTo(1.2d).Within(0.0001d));
            Assert.That(result.DishScores.Single(x => x.DishId == "none").Multiplier.ToDouble(),
                Is.EqualTo(1d).Within(0.0001d));
        }

        [Test]
        public void PermanentFlat_UsesDistinctScoreLineKindFromTemporaryFlat()
        {
            var table = new DiningTable(1, 1);
            table.Place(CreateDish(1, "dish", 10, 0));
            var specs = new[]
            {
                new ItemScoreSpec(ItemScoreEffectType.AllDishFlat, 2f, string.Empty, "temp", "临时加分"),
                new ItemScoreSpec(ItemScoreEffectType.PermanentAddFlatAll, 3f, string.Empty, "permanent", "永久加分"),
            };

            ScoreResult result = new ScoreCalculator(
                    effectSources: new[] { new ItemScoreEffectSource(specs) })
                .Calculate(table, CreateEmptyDatabase());

            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.DishFlat && line.Value == 2f), Is.True);
            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.DishPermanentFlat && line.Value == 3f), Is.True);
            Assert.That(result.DishScores.Single().FlatBonus.ToDouble(), Is.EqualTo(5d));
            Assert.That(result.PermanentFlatDeltas[1].ToDouble(), Is.EqualTo(3d));
        }

        [Test]
        public void CountThresholdAllDishMultiplier_TriggersBeforeAllAndTargetsEveryDish()
        {
            var table = new DiningTable(2, 1);
            table.Place(CreateDish(1, "first", 10, 0));
            table.Place(CreateDish(2, "second", 20, 1));
            var spec = new ItemScoreSpec(
                ItemScoreEffectType.CountThresholdAllDishMult,
                1.5f,
                "lte:2",
                "item_count_le_mult",
                "素色桌旗");

            ScoreResult result = new ScoreCalculator(
                    effectSources: new[] { new ItemScoreEffectSource(new[] { spec }) })
                .Calculate(table, CreateEmptyDatabase());

            Assert.That(result.FinalMultiplier.ToDouble(), Is.EqualTo(1d));
            Assert.That(result.Total.ToDouble(), Is.EqualTo(45d).Within(0.0001d));
            Assert.That(result.DishScores.All(score =>
                Math.Abs(score.Multiplier.ToDouble() - 1.5d) < 0.0001d), Is.True);

            ScoreLine[] multiplierLines = result.ScoreLines
                .Where(line => line.Kind == ScoreLineKind.DishMultiplier)
                .ToArray();
            Assert.That(multiplierLines, Has.Length.EqualTo(2));
            Assert.That(multiplierLines.All(line => line.Phase == ScorePhase.BeforeAll), Is.True);
            Assert.That(multiplierLines.All(line => line.Source.Id == "item_count_le_mult"), Is.True);
            Assert.That(multiplierLines[0].ExecutionGroupId, Is.GreaterThan(0));
            Assert.That(multiplierLines.Select(line => line.ExecutionGroupId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.FinalMultiplier), Is.False);
        }

        [Test]
        public void CountThresholdAllDishMultiplier_DoesNothingWhenThresholdFails()
        {
            var table = new DiningTable(2, 1);
            table.Place(CreateDish(1, "first", 10, 0));
            table.Place(CreateDish(2, "second", 20, 1));
            var spec = new ItemScoreSpec(
                ItemScoreEffectType.CountThresholdAllDishMult,
                1.5f,
                "lte:1",
                "item_count_le_mult",
                "素色桌旗");

            ScoreResult result = new ScoreCalculator(
                    effectSources: new[] { new ItemScoreEffectSource(new[] { spec }) })
                .Calculate(table, CreateEmptyDatabase());

            Assert.That(result.Total.ToDouble(), Is.EqualTo(30d).Within(0.0001d));
            Assert.That(result.ScoreLines.Any(line =>
                line.Source.Id == "item_count_le_mult"), Is.False);
        }

        private static DishInstance CreateDish(
            int instanceId,
            string id,
            int deliciousness,
            int x,
            params string[] skillIds)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var def = new DishDef(
                id,
                id,
                deliciousness,
                shape,
                0,
                0,
                1f,
                skillIds,
                string.Empty,
                allowRotate: false);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
                skillIds,
                Array.Empty<string>());
        }

        private static GameplayDatabase CreateEmptyDatabase()
        {
            return new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }
    }
}
