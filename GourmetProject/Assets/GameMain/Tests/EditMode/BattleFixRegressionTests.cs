using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleFixRegressionTests
    {
        [Test]
        public void NumbFlavor_RotationDeltaTracksAddsReplacementAndMultipleSteps()
        {
            FlavorDef numbOne = Flavor("numb_one", FlavorEffectType.Rotate, 1f);
            FlavorDef numbTwo = Flavor("numb_two", FlavorEffectType.Rotate, 2f);
            FlavorDef sweet = Flavor("sweet", FlavorEffectType.AddFlat, 1f);
            GameplayDatabase db = Database(flavors: new[] { numbOne, numbTwo, sweet });
            DishInstance dish = Dish(1, "dish", 0, 0, Array.Empty<string>(), new[] { "numb_one", "numb_two" });

            int before = BattleUseContext.NumbRotationSteps(dish.FlavorIds, db);
            Assert.That(before, Is.EqualTo(3));

            dish.AddFlavor("sweet", flavorLimit: 2);
            int afterFirstReplacement = BattleUseContext.NumbRotationSteps(dish.FlavorIds, db);
            Assert.That(afterFirstReplacement - before, Is.EqualTo(-1));

            dish.AddFlavor("sweet", flavorLimit: 2);
            int afterSecondReplacement = BattleUseContext.NumbRotationSteps(dish.FlavorIds, db);
            Assert.That(afterSecondReplacement - afterFirstReplacement, Is.EqualTo(-2));

            dish.AddFlavor("sweet", flavorLimit: 3);
            Assert.That(
                BattleUseContext.NumbRotationSteps(dish.FlavorIds, db) - afterSecondReplacement,
                Is.EqualTo(0));
        }

        [Test]
        public void RotationDelta_SignedStepsRotateBackAndMoveDishToTemporaryArea()
        {
            DishShape shape = DishShape.FromRows(new[] { "XX", "X." });
            DishInstance dish = Dish(1, "dish", 0, 0, Array.Empty<string>(), Array.Empty<string>(), shape);
            var table = new DiningTable(3, 3);
            table.Place(dish);
            var session = new BattleSession(
                table,
                Database(dishes: new[] { dish.Def }),
                new Xoshiro256SS(7UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            Assert.That(session.MoveDishToTemporaryAreaAfterRotationDelta(dish.Id, -1), Is.True);
            Assert.That(dish.Placement.RotationIndex, Is.EqualTo(1));
            Assert.That(table.DishCount, Is.EqualTo(0));
            Assert.That(session.TemporaryAreaDishes.Single(), Is.SameAs(dish));
        }

        [Test]
        public void DiningTable_UsesOnlyCanonicalDishOrientation()
        {
            DishShape shape = DishShape.FromRows(new[] { "XX" });
            DishDef dish = DishWithShape("horizontal", shape);
            var horizontalTable = new DiningTable(2, 1);

            List<Placement> placements = horizontalTable.FindValidPlacements(dish);

            Assert.That(placements, Has.Count.EqualTo(1));
            Assert.That(placements[0].RotationIndex, Is.Zero);
            Assert.That(placements[0].Orientation.Width, Is.EqualTo(2));
            Assert.That(placements[0].Orientation.Height, Is.EqualTo(1));
            Assert.That(new DiningTable(1, 2).FindValidPlacements(dish), Is.Empty);
        }

        [Test]
        public void GenerateDishAt_UsesCanonicalShapeAndDoesNotTryOtherOrientations()
        {
            DishShape shape = DishShape.FromRows(new[] { "XX" });
            DishDef dish = DishWithShape("horizontal", shape);
            var horizontalTable = new DiningTable(2, 1);
            var horizontalSession = new BattleSession(
                horizontalTable,
                Database(dishes: new[] { dish }),
                new Xoshiro256SS(11UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            Assert.That(horizontalSession.GenerateDishAt(dish.Id, new GridPos(0, 0)), Is.True);
            DishInstance generated = horizontalTable.Dishes.Single();
            Assert.That(generated.Placement.RotationIndex, Is.Zero);
            Assert.That(generated.Placement.Orientation.Width, Is.EqualTo(2));
            Assert.That(generated.Placement.Orientation.Height, Is.EqualTo(1));

            var verticalSession = new BattleSession(
                new DiningTable(1, 2),
                Database(dishes: new[] { dish }),
                new Xoshiro256SS(12UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            Assert.That(verticalSession.GenerateDishAt(dish.Id, new GridPos(0, 0)), Is.False);
        }

        [Test]
        public void NumbRotation_RotatesCanonicalShapeCounterClockwise()
        {
            DishShape shape = DishShape.FromRows(new[] { "XX" });
            DishDef dish = DishWithShape("horizontal", shape);
            var table = new DiningTable(1, 2);

            Assert.That(table.FindValidPlacements(dish), Is.Empty);

            List<Placement> placements = table.FindValidPlacementsRotatedCcw(dish, ccwSteps: 1);

            Assert.That(placements, Has.Count.EqualTo(1));
            Assert.That(placements[0].RotationIndex, Is.EqualTo(3));
            Assert.That(placements[0].Orientation.Width, Is.EqualTo(1));
            Assert.That(placements[0].Orientation.Height, Is.EqualTo(2));

            List<Placement> wrappedPlacements = table.FindValidPlacementsRotatedCcw(dish, ccwSteps: 5);
            Assert.That(wrappedPlacements, Has.Count.EqualTo(1));
            Assert.That(wrappedPlacements[0].RotationIndex, Is.EqualTo(3));
        }

        [Test]
        public void ActiveAddCountAs_AffectsOnlyRulesAfterItExecutes()
        {
            ScoreResult result = CalculateCountAsSequence();

            Assert.That(result.Total.ToDouble(), Is.EqualTo(21d).Within(0.0001d));
            Assert.That(result.DishScores.Single().EffectiveCountAs, Is.EqualTo(2));
        }

        [Test]
        public void PurpleRicePudding_AddsOccupiedCellsMinusOne_OnTopOfStaticServings()
        {
            const string skillId = "sk_purple_rice_pudding";
            SkillRuleDef rule = RuleFull(
                "purple_rice_pudding",
                skillId,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddCountAs,
                SkillScope.All,
                0,
                1f,
                "target:occupiedcells;offset:-1");
            var skill = Skill(skillId, rule);
            DishInstance purpleRicePudding = Dish(1, "purple_rice_pudding", 0, 0, new[] { skillId }, Array.Empty<string>());
            DishShape threeCells = DishShape.FromRows(new[] { "XXX" });
            DishInstance mango = Dish(2, "mango", 0, 1, Array.Empty<string>(), Array.Empty<string>(), threeCells, countAs: 8);
            var table = new DiningTable(4, 1);
            table.Place(purpleRicePudding);
            table.Place(mango);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { purpleRicePudding.Def, mango.Def }, skills: new[] { skill }));

            Assert.That(result.DishScores.Single(score => score.DishInstanceId == purpleRicePudding.Id).EffectiveCountAs, Is.EqualTo(1));
            Assert.That(result.DishScores.Single(score => score.DishInstanceId == mango.Id).EffectiveCountAs, Is.EqualTo(10));
        }

        [Test]
        public void DoubleSkinMilk_UsesEachTargetsOwnEffectiveServings_WithFractions()
        {
            const string skillId = "skill_apple";
            SkillRuleDef rule = RuleFull(
                "apple",
                skillId,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddMultFlat,
                SkillScope.All,
                0,
                0.5f,
                "source:target-countas");
            var skill = Skill(skillId, rule);
            DishInstance one = Dish(1, "one", 1, 0, new[] { skillId }, Array.Empty<string>(), countAs: 1);
            DishInstance two = Dish(2, "two", 1, 1, Array.Empty<string>(), Array.Empty<string>(), countAs: 2);
            DishInstance three = Dish(3, "three", 1, 2, Array.Empty<string>(), Array.Empty<string>(), countAs: 3);
            var table = new DiningTable(3, 1);
            table.Place(one);
            table.Place(two);
            table.Place(three);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { one.Def, two.Def, three.Def }, skills: new[] { skill }));

            Assert.That(result.DishScores.Single(score => score.DishInstanceId == one.Id).Multiplier.ToDouble(), Is.EqualTo(1.5d).Within(0.0001d));
            Assert.That(result.DishScores.Single(score => score.DishInstanceId == two.Id).Multiplier.ToDouble(), Is.EqualTo(2d).Within(0.0001d));
            Assert.That(result.DishScores.Single(score => score.DishInstanceId == three.Id).Multiplier.ToDouble(), Is.EqualTo(2.5d).Within(0.0001d));
        }

        [Test]
        public void FruitCake_HalfOfFifteenExistingCells_RoundsUpToEightLayers()
        {
            const string skillId = "skill_fruit_cake";
            SkillRuleDef rule = RuleFull(
                "fruit_cake",
                skillId,
                SkillConditionType.OccupiedCell,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Per,
                "source:board",
                SkillActionType.AddLayer,
                SkillScope.CakeBuff,
                0,
                0.5f,
                "round:ceil");
            var skill = Skill(skillId, rule);
            DishInstance dish = Dish(1, "fruit_cake", 0, 0, new[] { skillId }, Array.Empty<string>());
            var table = new DiningTable(5, 3);
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { dish.Def }, skills: new[] { skill }));

            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(8));
        }

        [Test]
        public void Cake03_RandomlySelectsOneDistinctLeftDish_WithoutCellWeight()
        {
            const string skillId = "skill_cake_03";
            SkillRuleDef rule = RuleFull(
                "cake_03_category",
                skillId,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddTemporaryCategory,
                SkillScope.Left,
                1,
                0f,
                "cat:cake;target:random");
            SkillDef skill = Skill(skillId, rule);
            DishInstance oneCell = Dish(1, "one_cell", 0, 0, Array.Empty<string>(), Array.Empty<string>());
            DishInstance twoCells = Dish(
                2,
                "two_cells",
                0,
                3,
                Array.Empty<string>(),
                Array.Empty<string>(),
                DishShape.FromRows(new[] { "XX" }));
            DishInstance cake = Dish(3, "cake_03", 0, 6, new[] { skillId }, Array.Empty<string>());
            var table = new DiningTable(8, 1);
            table.Place(oneCell);
            table.Place(twoCells);
            table.Place(cake);
            int randomCalls = 0;

            ScoreResult preview = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { oneCell.Def, twoCells.Def, cake.Def }, skills: new[] { skill }));
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { oneCell.Def, twoCells.Def, cake.Def }, skills: new[] { skill }),
                randomIntegerSelector: (min, max) =>
                {
                    randomCalls++;
                    Assert.That(min, Is.Zero);
                    Assert.That(max, Is.EqualTo(1));
                    return max;
                });

            Assert.That(preview.TemporaryCategories.Single().DishInstanceId, Is.EqualTo(oneCell.Id));
            Assert.That(result.TemporaryCategories.Single().DishInstanceId, Is.EqualTo(twoCells.Id));
            Assert.That(result.TemporaryCategories.Single().Category, Is.EqualTo("cake"));
            Assert.That(randomCalls, Is.EqualTo(1));
        }

        [Test]
        public void Cake03_WithNoLeftCandidate_StillConsumesTenLayers()
        {
            const string skillId = "skill_cake_03";
            SkillRuleDef categoryRule = RuleFull(
                "cake_03_category",
                skillId,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddTemporaryCategory,
                SkillScope.Left,
                1,
                0f,
                "cat:cake;target:random");
            SkillRuleDef consumeRule = new SkillRuleDef(
                "cake_03_consume",
                skillId,
                1,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.ConsumeLayer,
                SkillScope.CakeBuff,
                0,
                new[] { 10f },
                Array.Empty<string>());
            SkillDef skill = Skill(skillId, categoryRule, consumeRule);
            DishInstance cake = Dish(1, "cake_03", 0, 0, new[] { skillId }, Array.Empty<string>());
            var table = new DiningTable(3, 1);
            table.Place(cake);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { cake.Def }, skills: new[] { skill }),
                initialHappyCakeLayers: 20);

            Assert.That(result.TemporaryCategories, Is.Empty);
            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(-10));
        }

        [Test]
        public void MultipleCake03Dishes_RollTheirLeftTargetsIndependently()
        {
            const string skillId = "skill_cake_03";
            SkillRuleDef rule = RuleFull(
                "cake_03_category",
                skillId,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddTemporaryCategory,
                SkillScope.Left,
                1,
                0f,
                "cat:cake;target:random");
            SkillDef skill = Skill(skillId, rule);
            DishInstance topLeft = Dish(1, "top_left", 0, 0, Array.Empty<string>(), Array.Empty<string>(), y: 0);
            DishInstance topRight = Dish(2, "top_right", 0, 2, Array.Empty<string>(), Array.Empty<string>(), y: 0);
            DishInstance topCake = Dish(3, "top_cake", 0, 4, new[] { skillId }, Array.Empty<string>(), y: 0);
            DishInstance bottomLeft = Dish(4, "bottom_left", 0, 0, Array.Empty<string>(), Array.Empty<string>(), y: 1);
            DishInstance bottomRight = Dish(5, "bottom_right", 0, 2, Array.Empty<string>(), Array.Empty<string>(), y: 1);
            DishInstance bottomCake = Dish(6, "bottom_cake", 0, 4, new[] { skillId }, Array.Empty<string>(), y: 1);
            var table = new DiningTable(5, 2);
            table.Place(topLeft);
            table.Place(topRight);
            table.Place(topCake);
            table.Place(bottomLeft);
            table.Place(bottomRight);
            table.Place(bottomCake);
            int randomCalls = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(
                    dishes: new[]
                    {
                        topLeft.Def,
                        topRight.Def,
                        topCake.Def,
                        bottomLeft.Def,
                        bottomRight.Def,
                        bottomCake.Def
                    },
                    skills: new[] { skill }),
                randomIntegerSelector: (min, max) =>
                {
                    randomCalls++;
                    return max;
                });

            Assert.That(randomCalls, Is.EqualTo(2));
            Assert.That(
                result.TemporaryCategories.Select(effect => effect.DishInstanceId),
                Is.EquivalentTo(new[] { topRight.Id, bottomRight.Id }));
        }

        [Test]
        public void EmptyCellServings_StackToFourPerCell_AndResetWithContext()
        {
            SkillRuleDef emptyRule = RuleFull(
                "empty",
                "skill_empty",
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddEmptyCountAs,
                SkillScope.All,
                0,
                2f,
                string.Empty);
            SkillRuleDef countRule = RuleFull(
                "count",
                "skill_count",
                SkillConditionType.DishCount,
                SkillScope.All,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                1f,
                string.Empty);
            SkillDef emptySkill = Skill("skill_empty", emptyRule);
            SkillDef countSkill = Skill("skill_count", countRule);
            DishInstance first = Dish(1, "first", 0, 0, new[] { "skill_empty" }, Array.Empty<string>());
            DishInstance second = Dish(2, "second", 0, 1, new[] { "skill_empty" }, Array.Empty<string>());
            DishInstance counter = Dish(3, "counter", 0, 2, new[] { "skill_count" }, Array.Empty<string>());
            var table = new DiningTable(5, 1);
            table.Place(first);
            table.Place(second);
            table.Place(counter);
            GameplayDatabase db = Database(
                dishes: new[] { first.Def, second.Def, counter.Def },
                skills: new[] { emptySkill, countSkill });

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreResult secondCalculation = new ScoreCalculator().Calculate(table, db);

            Assert.That(result.DishScores.Single(score => score.DishInstanceId == counter.Id).FlatBonus.ToDouble(), Is.EqualTo(11d));
            Assert.That(secondCalculation.DishScores.Single(score => score.DishInstanceId == counter.Id).FlatBonus.ToDouble(), Is.EqualTo(11d));
        }

        [Test]
        public void PhysicalInstances_DoNotUseEffectiveServings()
        {
            const string skillId = "skill_physical";
            SkillRuleDef rule = RuleFull(
                "physical",
                skillId,
                SkillConditionType.DishCount,
                SkillScope.All,
                CountUnit.PhysicalInstances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                1f,
                string.Empty);
            var skill = Skill(skillId, rule);
            DishInstance source = Dish(1, "source", 0, 0, new[] { skillId }, Array.Empty<string>(), countAs: 3);
            DishInstance other = Dish(2, "other", 0, 1, Array.Empty<string>(), Array.Empty<string>(), countAs: 8);
            var table = new DiningTable(2, 1);
            table.Place(source);
            table.Place(other);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { source.Def, other.Def }, skills: new[] { skill }));

            Assert.That(result.DishScores.Single(score => score.DishInstanceId == source.Id).FlatBonus.ToDouble(), Is.EqualTo(2d));
        }

        [Test]
        public void Gummy_MultipliesOnlySuccessfulTransferSource_OncePerSuccess()
        {
            SkillRuleDef gummyRule = RuleFull(
                "gummy",
                "skill_gummy",
                SkillConditionType.None,
                SkillScope.ColumnAndSelf,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddMult,
                SkillScope.ColumnAndSelf,
                0,
                1.5f,
                "when:transfer;resultscope:TransferSource");
            SkillRuleDef payload = RuleFull(
                "payload",
                "skill_transfer",
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                0,
                1f,
                string.Empty);
            SkillRuleDef firstTransfer = RuleFull(
                "transfer_1",
                "skill_transfer",
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.Other,
                1,
                0f,
                string.Empty);
            SkillRuleDef secondTransfer = new SkillRuleDef(
                "transfer_2",
                "skill_transfer",
                2,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.Other,
                1,
                new[] { 0f },
                Array.Empty<string>());
            SkillDef gummySkill = Skill("skill_gummy", gummyRule);
            SkillDef transferSkill = Skill("skill_transfer", payload, firstTransfer, secondTransfer);
            DishInstance gummy = Dish(1, "gummy", 0, 0, new[] { "skill_gummy" }, Array.Empty<string>(), y: 0);
            DishInstance target = Dish(2, "target", 0, 1, Array.Empty<string>(), Array.Empty<string>(), y: 0);
            DishInstance source = Dish(3, "source", 0, 0, new[] { "skill_transfer" }, Array.Empty<string>(), y: 1);
            var table = new DiningTable(2, 2);
            table.Place(gummy);
            table.Place(target);
            table.Place(source);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(
                    dishes: new[] { gummy.Def, target.Def, source.Def },
                    skills: new[] { gummySkill, transferSkill }));

            Assert.That(result.DishScores.Single(score => score.DishInstanceId == source.Id).Multiplier.ToDouble(), Is.EqualTo(2.25d).Within(0.0001d));
            Assert.That(result.DishScores.Single(score => score.DishInstanceId == gummy.Id).Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
        }

        [Test]
        public void RecipeRemoval_IsRolledOnlyByOfficialSettlement_AndKeepsRecipeIndex()
        {
            const string skillId = "skill_remove";
            SkillRuleDef rule = RuleFull(
                "remove",
                skillId,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                SkillActionType.RequestRecipeRemoval,
                SkillScope.Self,
                0,
                1f,
                string.Empty);
            SkillDef skill = Skill(skillId, rule);
            DishInstance dish = Dish(1, "remove_me", 1, 0, new[] { skillId }, Array.Empty<string>());
            dish.SetSourceRecipeIndex(0, 4);
            var table = new DiningTable(1, 1);
            table.Place(dish);
            GameplayDatabase db = Database(dishes: new[] { dish.Def }, skills: new[] { skill });
            var session = new BattleSession(
                table,
                db,
                new Xoshiro256SS(123UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 1);

            Assert.That(session.PreviewScore().RecipeRemovalRequests.Count, Is.EqualTo(1));
            Assert.That(session.LastRecipeRemovalOutcomes, Is.Empty);

            session.Settle();

            Assert.That(session.IsWin, Is.True);
            Assert.That(session.LastRecipeRemovalOutcomes.Count, Is.EqualTo(1));
            Assert.That(session.LastRecipeRemovalOutcomes[0].Removed, Is.True);
            Assert.That(session.LastRecipeRemovalOutcomes[0].Request.SourceDishIndex, Is.EqualTo(4));
        }

        [TestCase(999999999d, "999999999")]
        [TestCase(1000000000d, "1e9")]
        [TestCase(-1236000000d, "-1.24e9")]
        public void ScoreNumberFormatter_UsesExpectedThresholdAndRounding(double value, string expected)
        {
            Assert.That(ScoreNumberFormatter.Format(new BigDouble(value)), Is.EqualTo(expected));
        }

        [Test]
        public void ScoreNumberFormatter_AndTotalSupportValuesBeyondDoubleRange()
        {
            BigDouble raw = BigDouble.Normalize(9d, 400);
            var result = new ScoreResult(
                Array.Empty<DishScore>(),
                raw,
                BigDouble.Zero,
                BigDouble.Normalize(2d, 100));

            Assert.That(result.Total, Is.GreaterThan(BigDouble.Zero));
            Assert.That(result.Total.Exponent, Is.EqualTo(501));
            Assert.That(ScoreNumberFormatter.Format(BigDouble.Normalize(9.99d, 400)), Is.EqualTo("9.99e400"));
        }

        [Test]
        public void BigNumberSaveData_RoundTripsAndSaturatesLegacyFields()
        {
            BigDouble original = BigDouble.Normalize(1.234d, 450);
            BigNumberSaveData saved = BigNumberSaveData.From(original);
            BigDouble restored = saved.GetValue(17);

            Assert.That(restored.Mantissa, Is.EqualTo(original.Mantissa).Within(0.0000001d));
            Assert.That(restored.Exponent, Is.EqualTo(original.Exponent));
            Assert.That(BigNumberSaveData.ToLegacyInt(original), Is.EqualTo(int.MaxValue));
            Assert.That(BigNumberSaveData.ToLegacyFloat(original), Is.EqualTo(float.MaxValue));
            Assert.That(new BigNumberSaveData().GetValue(17).ToDouble(), Is.EqualTo(17d));
        }

        [Test]
        public void DirectServe_AutoConfirmsOnlyServeActions()
        {
            Assert.That(BattleWorldController.ShouldAutoConfirmPendingDish(PendingDishActionKind.Serve, true), Is.True);
            Assert.That(BattleWorldController.ShouldAutoConfirmPendingDish(PendingDishActionKind.Confirm, true), Is.False);
            Assert.That(BattleWorldController.ShouldAutoConfirmPendingDish(PendingDishActionKind.Serve, false), Is.False);
        }

        [Test]
        public void SettingsPrefab_HasTenNonNullRows()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/SettingsForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            SettingsForm form = prefab.GetComponent<SettingsForm>();
            Assert.That(form, Is.Not.Null);

            var serialized = new SerializedObject(form);
            SerializedProperty rows = serialized.FindProperty("_settingRows");
            Assert.That(rows.arraySize, Is.EqualTo(10));
            for (int i = 0; i < rows.arraySize; i++)
            {
                Assert.That(rows.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"row {i}");
            }

            Transform listTransform = prefab.transform.Find("SettingsList");
            Transform viewport = prefab.transform.Find("SettingsList/Viewport");
            Transform content = prefab.transform.Find("SettingsList/Viewport/Content");
            Transform scrollbarTransform = prefab.transform.Find("SettingsList/Scrollbar");
            Transform scrollbarHandle = prefab.transform.Find("SettingsList/Scrollbar/Sliding Area/Handle");
            Assert.That(listTransform, Is.Not.Null);
            Assert.That(viewport, Is.Not.Null);
            Assert.That(content, Is.Not.Null);
            Assert.That(scrollbarTransform, Is.Not.Null);
            Assert.That(scrollbarHandle, Is.Not.Null);
            Assert.That(content.childCount, Is.EqualTo(10));

            ScrollRect scrollRect = listTransform.GetComponent<ScrollRect>();
            Assert.That(scrollRect, Is.Not.Null);
            Assert.That(scrollRect.content, Is.SameAs(content));
            Assert.That(scrollRect.viewport, Is.SameAs(viewport));
            Assert.That(scrollRect.vertical, Is.True);
            Assert.That(scrollRect.horizontal, Is.False);

            Scrollbar scrollbar = scrollbarTransform.GetComponent<Scrollbar>();
            Assert.That(scrollbar, Is.Not.Null);
            Assert.That(scrollRect.verticalScrollbar, Is.Null,
                "The visible scrollbar must not be assigned here, otherwise ScrollRect rewrites its size every frame.");
            Assert.That(scrollRect.verticalScrollbarVisibility, Is.EqualTo(ScrollRect.ScrollbarVisibility.Permanent));
            Assert.That(scrollbar.handleRect, Is.SameAs(scrollbarHandle));
            Assert.That(scrollbar.direction, Is.EqualTo(Scrollbar.Direction.BottomToTop));

            FixedScrollbarHandleSize fixedHandleSize = scrollbarTransform.GetComponent<FixedScrollbarHandleSize>();
            Assert.That(fixedHandleSize, Is.Not.Null);
            var fixedHandleSerialized = new SerializedObject(fixedHandleSize);
            Assert.That(fixedHandleSerialized.FindProperty("_scrollRect").objectReferenceValue, Is.SameAs(scrollRect));
            var slidingArea = scrollbar.handleRect.parent as RectTransform;
            Assert.That(slidingArea, Is.Not.Null);
            Assert.That(scrollbar.size * slidingArea.rect.height, Is.EqualTo(199f).Within(0.5f));

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            Assert.That(fitter, Is.Not.Null);
            Assert.That(fitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
        }

        [Test]
        public void SettlementPause_IsIdempotentAndRestoresOriginalTimeScaleOnce()
        {
            float original = Time.timeScale;
            var go = new GameObject("SettlementSequencerTest");
            SettlementSequencer sequencer = go.AddComponent<SettlementSequencer>();
            try
            {
                Time.timeScale = 0.65f;
                Assert.That(sequencer.PausePlayback(), Is.True);
                Assert.That(sequencer.PausePlayback(), Is.False);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(sequencer.ResumePlayback(), Is.True);
                Assert.That(sequencer.ResumePlayback(), Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(0.65f).Within(0.0001f));

                Assert.That(sequencer.PausePlayback(), Is.True);
                sequencer.ForceRestorePlaybackTimeScale();
                Assert.That(Time.timeScale, Is.EqualTo(0.65f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                Time.timeScale = original;
            }
        }

        private static ScoreResult CalculateCountAsSequence()
        {
            const string skillId = "skill_count_as";
            SkillRuleDef before = Rule(
                "before",
                skillId,
                0,
                SkillConditionType.DishCount,
                SkillScope.All,
                CountMode.Per,
                SkillActionType.AddFlat,
                SkillScope.Self,
                1f);
            SkillRuleDef addCountAs = Rule(
                "count_as",
                skillId,
                1,
                SkillConditionType.None,
                SkillScope.Self,
                CountMode.Gate,
                SkillActionType.AddCountAs,
                SkillScope.All,
                1f);
            SkillRuleDef after = Rule(
                "after",
                skillId,
                2,
                SkillConditionType.DishCount,
                SkillScope.All,
                CountMode.Per,
                SkillActionType.AddFlat,
                SkillScope.Self,
                10f);
            var skill = new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { before, addCountAs, after });
            DishInstance dish = Dish(1, "dish", 0, 0, new[] { skillId }, Array.Empty<string>());
            var table = new DiningTable(1, 1);
            table.Place(dish);

            return new ScoreCalculator().Calculate(
                table,
                Database(dishes: new[] { dish.Def }, skills: new[] { skill }));
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            int order,
            SkillConditionType condition,
            SkillScope conditionScope,
            CountMode countMode,
            SkillActionType action,
            SkillScope actionScope,
            float value)
        {
            return new SkillRuleDef(
                id,
                skillId,
                order,
                SkillTrigger.OnSettle,
                condition,
                conditionScope,
                CountUnit.Instances,
                countMode,
                string.Empty,
                action,
                actionScope,
                0,
                new[] { value },
                Array.Empty<string>());
        }

        private static SkillRuleDef RuleFull(
            string id,
            string skillId,
            SkillConditionType condition,
            SkillScope conditionScope,
            CountUnit countUnit,
            CountMode countMode,
            string conditionParam,
            SkillActionType action,
            SkillScope actionScope,
            int actionCount,
            float value,
            string actionParam)
        {
            return new SkillRuleDef(
                id,
                skillId,
                0,
                SkillTrigger.OnSettle,
                condition,
                conditionScope,
                countUnit,
                countMode,
                conditionParam,
                action,
                actionScope,
                actionCount,
                new[] { value },
                string.IsNullOrEmpty(actionParam) ? Array.Empty<string>() : new[] { actionParam });
        }

        private static SkillDef Skill(string id, params SkillRuleDef[] rules)
        {
            return new SkillDef(
                id,
                id,
                string.Empty,
                Array.Empty<string>(),
                rules);
        }

        private static FlavorDef Flavor(string id, FlavorEffectType type, float value)
        {
            return new FlavorDef(
                id,
                id,
                string.Empty,
                type,
                new[] { value },
                Array.Empty<string>(),
                string.Empty);
        }

        private static DishDef DishWithShape(string id, DishShape shape)
        {
            return new DishDef(
                id,
                id,
                0,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
        }

        private static DishInstance Dish(
            int instanceId,
            string id,
            int deliciousness,
            int x,
            IReadOnlyList<string> skillIds,
            IReadOnlyList<string> flavorIds,
            DishShape shape = null,
            int countAs = 1,
            int y = 0)
        {
            shape ??= DishShape.FromRows(new[] { "X" });
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
                countAs: countAs);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                skillIds,
                flavorIds);
        }

        private static GameplayDatabase Database(
            IReadOnlyList<DishDef> dishes = null,
            IReadOnlyList<SkillDef> skills = null,
            IReadOnlyList<FlavorDef> flavors = null)
        {
            return new GameplayDatabase(
                dishes ?? Array.Empty<DishDef>(),
                skills ?? Array.Empty<SkillDef>(),
                flavors ?? Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }
    }
}
