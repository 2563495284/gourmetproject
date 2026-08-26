using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SaltyExtraSettlementTests
    {
        [Test]
        public void Hit_ReplaysNativeSkillIntoNormalScoreWithoutIndependentContribution()
        {
            SkillDef nativeSkill = CreateAddFlatSkill("native", 40f);
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 1f);
            TestBoard test = CreateSingleDishBoard(30, new[] { nativeSkill }, new[] { salty });

            ScoreResult result = CalculateHit(test);

            DishScore score = result.DishScores.Single();
            Assert.That(Value(score.FlatBonus), Is.EqualTo(80d).Within(1e-9));
            Assert.That(Value(score.Contribution), Is.EqualTo(110d).Within(1e-9));
            Assert.That(Value(score.ExtraSettlementContribution), Is.Zero.Within(1e-9));
            Assert.That(score.ExtraSettlementCount, Is.EqualTo(1));

            ScoreLine trigger = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.ExtraSettlement);
            Assert.That(Value(trigger.Value), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Value(trigger.Before), Is.Zero.Within(1e-9));
            Assert.That(Value(trigger.After), Is.EqualTo(1d).Within(1e-9));

            int triggerIndex = result.ScoreLines
                .Select((line, index) => (line, index))
                .Single(item => ReferenceEquals(item.line, trigger))
                .index;
            int repeatedSkillIndex = result.ScoreLines
                .Select((line, index) => (line, index))
                .Where(item => item.line.Kind == ScoreLineKind.DishFlat
                    && item.line.Trace?.Kind == SkillExecutionKind.NativeSkill)
                .Select(item => item.index)
                .Last();
            Assert.That(repeatedSkillIndex, Is.GreaterThan(triggerIndex));
        }

        [Test]
        public void Hit_ReplaysOnlyNativeSkills()
        {
            SkillDef nativeSkill = CreateAddFlatSkill("native", 10f);
            SkillDef copiedSkill = CreateAddFlatSkill("copied", 100f);
            SkillDef transferredParent = CreateAddFlatSkill("transferred", 1000f);
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 1f);
            TestBoard test = CreateSingleDishBoard(
                30,
                new[] { nativeSkill, copiedSkill, transferredParent },
                new[] { salty },
                initialSkillIds: new[] { nativeSkill.Id });
            test.Dish.AddSkill(copiedSkill.Id, "复制来源<技能复制>");
            test.Dish.AddTransferredSkill(
                new SkillEffect(transferredParent.Rules[0], string.Empty),
                "传递来源<甜蜜传递>",
                sourceInstanceId: 99);

            ScoreResult result = CalculateHit(test);

            DishScore score = result.DishScores.Single();
            Assert.That(Value(score.FlatBonus), Is.EqualTo(1120d).Within(1e-9));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.DishFlat
                    && line.Trace?.Kind == SkillExecutionKind.NativeSkill),
                Is.EqualTo(2));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.DishFlat
                    && line.Trace?.Kind == SkillExecutionKind.CopiedSkill),
                Is.EqualTo(1));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.DishFlat
                    && line.Trace?.Kind == SkillExecutionKind.SweetTransfer),
                Is.EqualTo(1));
        }

        [Test]
        public void Hit_DoesNotReplayOtherFlavorEffects()
        {
            SkillDef nativeSkill = CreateAddFlatSkill("native", 10f);
            FlavorDef seasoning = CreateFlavor("seasoning", FlavorEffectType.AddFlat, 7f);
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 1f);
            TestBoard test = CreateSingleDishBoard(
                30,
                new[] { nativeSkill },
                new[] { seasoning, salty });

            ScoreResult result = CalculateHit(test);

            DishScore score = result.DishScores.Single();
            Assert.That(Value(score.FlatBonus), Is.EqualTo(27d).Within(1e-9));
        }

        [Test]
        public void NativeSkill_RemainsNativeWhenDuplicateAcquisitionAddsASourceLabel()
        {
            SkillDef nativeSkill = CreateAddFlatSkill("native", 10f);
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 1f);
            TestBoard test = CreateSingleDishBoard(30, new[] { nativeSkill }, new[] { salty });
            test.Dish.AddSkill(nativeSkill.Id, "重复来源<技能复制>");

            ScoreResult result = CalculateHit(test);

            DishScore score = result.DishScores.Single();
            Assert.That(Value(score.FlatBonus), Is.EqualTo(20d).Within(1e-9));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.DishFlat
                    && line.Trace?.Kind == SkillExecutionKind.NativeSkill),
                Is.EqualTo(2));
        }

        [Test]
        public void MultipleHits_ReplayNativeSkillEachTimeEvenWithoutDiagnostics()
        {
            SkillDef nativeSkill = CreateAddFlatSkill("native", 10f);
            FlavorDef saltyA = CreateFlavor("salty_a", FlavorEffectType.ExtraSettlementChance, 1f);
            FlavorDef saltyB = CreateFlavor("salty_b", FlavorEffectType.ExtraSettlementChance, 1f);
            TestBoard test = CreateSingleDishBoard(
                30,
                new[] { nativeSkill },
                new[] { saltyA, saltyB });

            ScoreResult result = new ScoreCalculator().Calculate(
                test.Board,
                test.Database,
                randomIntegerSelector: (_, _) => 0,
                captureDiagnostics: false);

            DishScore score = result.DishScores.Single();
            Assert.That(Value(score.FlatBonus), Is.EqualTo(30d).Within(1e-9));
            Assert.That(score.ExtraSettlementCount, Is.EqualTo(2));
            Assert.That(result.ScoreLines, Is.Empty);
        }

        [Test]
        public void Miss_LeavesNativeSkillAtOneExecution()
        {
            SkillDef nativeSkill = CreateAddFlatSkill("native", 40f);
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 0.2f);
            TestBoard test = CreateSingleDishBoard(30, new[] { nativeSkill }, new[] { salty });

            ScoreResult result = new ScoreCalculator().Calculate(
                test.Board,
                test.Database,
                randomIntegerSelector: (_, _) => 9999);

            DishScore score = result.DishScores.Single();
            Assert.That(Value(score.FlatBonus), Is.EqualTo(40d).Within(1e-9));
            Assert.That(score.ExtraSettlementCount, Is.Zero);
            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.ExtraSettlement), Is.False);
        }

        [Test]
        public void Hit_ReappliesNativeSkillEffectsToOtherDishes()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef nativeSkill = CreateAddFlatSkill("native", 5f, SkillScope.Other);
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 1f);
            DishDef sourceDef = CreateDish("source", 30, shape, new[] { nativeSkill.Id });
            DishDef targetDef = CreateDish("target", 20, shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { nativeSkill },
                new[] { salty },
                Array.Empty<RecipeDef>());
            var board = new DiningTable(2, 1);
            DishInstance source = CreateInstance(1, sourceDef, shape, 0, new[] { salty.Id });
            DishInstance target = CreateInstance(2, targetDef, shape, 1, Array.Empty<string>());
            board.Place(source);
            board.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                randomIntegerSelector: (_, _) => 0);

            DishScore targetScore = result.DishScores.Single(score => score.DishInstanceId == target.Id);
            Assert.That(Value(targetScore.FlatBonus), Is.EqualTo(10d).Within(1e-9));
            Assert.That(Value(targetScore.Contribution), Is.EqualTo(30d).Within(1e-9));
        }

        [Test]
        public void ExtraSettlementMarker_DoesNotChangePresentationLedgerValue()
        {
            var score = new DishScore(1, "dish", 30f, 0f, 1f);
            var ledger = new SettlementRunningLedger(new[] { score }, baselineSnapshot: null);
            ledger.ApplyBase(score.DishInstanceId, score.BaseValue);
            BigDouble before = ledger.CurrentTotal;
            FlavorDef salty = CreateFlavor("salty", FlavorEffectType.ExtraSettlementChance, 1f);
            var line = new ScoreLine(
                ScorePhase.AfterDish,
                ScoreLineKind.ExtraSettlement,
                ScoreSource.DishFlavor(salty, null),
                score.DishInstanceId,
                score.DishId,
                null,
                1f,
                0f,
                1f,
                "额外结算");

            BigDouble dishContribution = ledger.Apply(line);

            Assert.That(Value(dishContribution), Is.EqualTo(30d).Within(1e-9));
            Assert.That(Value(ledger.CurrentTotal), Is.EqualTo(Value(before)).Within(1e-9));
        }

        [Test]
        public void LegacyIndependentContribution_RemainsSupportedForSavedResults()
        {
            var score = new DishScore(
                1,
                "dish",
                30f,
                40f,
                1f,
                extraSettlementContribution: 70f,
                extraSettlementCount: 1);

            Assert.That(Value(score.Contribution), Is.EqualTo(140d).Within(1e-9));
        }

        private static ScoreResult CalculateHit(TestBoard test)
            => new ScoreCalculator().Calculate(
                test.Board,
                test.Database,
                randomIntegerSelector: (_, _) => 0);

        private static SkillDef CreateAddFlatSkill(
            string id,
            float value,
            SkillScope actionScope = SkillScope.Self)
        {
            var rule = new SkillRuleDef(
                $"{id}_rule",
                id,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                actionScope,
                actionCount: 0,
                new[] { value },
                Array.Empty<string>());
            return new SkillDef(
                id,
                id,
                string.Empty,
                Array.Empty<string>(),
                new[] { rule },
                new[] { rule.Id });
        }

        private static FlavorDef CreateFlavor(string id, FlavorEffectType type, float value)
            => new FlavorDef(
                id,
                id,
                string.Empty,
                type,
                new[] { value },
                Array.Empty<string>(),
                id);

        private static TestBoard CreateSingleDishBoard(
            int deliciousness,
            IReadOnlyList<SkillDef> skills,
            IReadOnlyList<FlavorDef> flavors,
            IReadOnlyList<string> initialSkillIds = null)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            IReadOnlyList<string> skillIds = initialSkillIds ?? skills.Select(skill => skill.Id).ToArray();
            DishDef dishDef = CreateDish("dish", deliciousness, shape, skillIds);
            var database = new GameplayDatabase(
                new[] { dishDef },
                skills,
                flavors,
                Array.Empty<RecipeDef>());
            var board = new DiningTable(1, 1);
            DishInstance dish = CreateInstance(
                1,
                dishDef,
                shape,
                0,
                flavors.Select(flavor => flavor.Id).ToArray());
            board.Place(dish);
            return new TestBoard(board, database, dish);
        }

        private static DishDef CreateDish(
            string id,
            int deliciousness,
            DishShape shape,
            IReadOnlyList<string> skillIds)
            => new DishDef(
                id,
                id,
                deliciousness,
                shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds,
                flavorId: string.Empty);

        private static DishInstance CreateInstance(
            int id,
            DishDef def,
            DishShape shape,
            int x,
            IReadOnlyList<string> flavorIds)
            => new DishInstance(
                id,
                def,
                new Placement(shape, rotationIndex: 0, new GridPos(x, 0)),
                def.SkillIds,
                flavorIds);

        private static double Value(BigDouble value) => value.ToDouble();

        private sealed class TestBoard
        {
            public TestBoard(DiningTable board, GameplayDatabase database, DishInstance dish)
            {
                Board = board;
                Database = database;
                Dish = dish;
            }

            public DiningTable Board { get; }

            public GameplayDatabase Database { get; }

            public DishInstance Dish { get; }
        }
    }
}
