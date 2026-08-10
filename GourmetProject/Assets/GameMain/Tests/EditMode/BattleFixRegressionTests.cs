using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Save;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
        public void ActiveAddCountAs_AffectsOnlyRulesAfterItExecutes()
        {
            ScoreResult result = CalculateCountAsSequence(isPassive: false);

            Assert.That(result.Total.ToDouble(), Is.EqualTo(21d).Within(0.0001d));
            Assert.That(result.DishScores.Single().EffectiveCountAs, Is.EqualTo(2));
        }

        [Test]
        public void PassiveAddCountAs_IsAvailableFromSettlementStart()
        {
            ScoreResult result = CalculateCountAsSequence(isPassive: true);

            Assert.That(result.Total.ToDouble(), Is.EqualTo(22d).Within(0.0001d));
            Assert.That(result.DishScores.Single().EffectiveCountAs, Is.EqualTo(2));
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

        private static ScoreResult CalculateCountAsSequence(bool isPassive)
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
                1f,
                isPassive);
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
            float value,
            bool isPassive = false)
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
                Array.Empty<string>(),
                isPassive: isPassive);
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

        private static DishInstance Dish(
            int instanceId,
            string id,
            int deliciousness,
            int x,
            IReadOnlyList<string> skillIds,
            IReadOnlyList<string> flavorIds,
            DishShape shape = null)
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
                allowRotate: true);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
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
