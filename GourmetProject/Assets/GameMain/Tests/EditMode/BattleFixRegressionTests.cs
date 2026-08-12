using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public void ActiveAddCountAs_AffectsOnlyRulesAfterItExecutes()
        {
            ScoreResult result = CalculateCountAsSequence();

            Assert.That(result.Total.ToDouble(), Is.EqualTo(21d).Within(0.0001d));
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
        public void RequireServeConfirmation_AutoConfirmsOnlyWhenDisabled()
        {
            Assert.That(BattleWorldController.ShouldAutoConfirmPendingDish(PendingDishActionKind.Serve, true), Is.False);
            Assert.That(BattleWorldController.ShouldAutoConfirmPendingDish(PendingDishActionKind.Serve, false), Is.True);
            Assert.That(BattleWorldController.ShouldAutoConfirmPendingDish(PendingDishActionKind.Confirm, false), Is.False);
            Assert.That(BattleWorldController.ShouldShowPendingDishActionButton(PendingDishActionKind.Serve, true), Is.True);
            Assert.That(BattleWorldController.ShouldShowPendingDishActionButton(PendingDishActionKind.Serve, false), Is.False);
            Assert.That(BattleWorldController.ShouldShowPendingDishActionButton(PendingDishActionKind.Confirm, false), Is.True);
        }

        [Test]
        public void PendingDishConfirmation_UsesPresentationCallbackOrFallback()
        {
            int presented = 0;
            int fallback = 0;
            BattleWorldController.DispatchPendingDishConfirmation(7, id => presented = id, id => fallback = id);
            Assert.That(presented, Is.EqualTo(7));
            Assert.That(fallback, Is.Zero);

            presented = 0;
            BattleWorldController.DispatchPendingDishConfirmation(9, null, id => fallback = id);
            Assert.That(presented, Is.Zero);
            Assert.That(fallback, Is.EqualTo(9));
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

        [Test]
        public void TableInspection_RemainsAvailableDuringSettlementOnlyBusyState()
        {
            var go = new GameObject("BattleWorldControllerTest");
            BattleWorldController world = go.AddComponent<BattleWorldController>();
            try
            {
                SetPrivateField(world, "_worldMode", "Food");
                SetPrivateField(world, "_settling", true);

                Assert.That(world.CanEnterTableView, Is.False);
                Assert.That(world.CanEnterTableInspectionView, Is.True);

                SetPrivateField(world, "_activeItemTransitioning", true);
                Assert.That(world.CanEnterTableInspectionView, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SettingsSettlementPause_IsOwnedAndReleasedExactlyOnce()
        {
            int pauseCount = 0;
            int resumeCount = 0;
            var data = new SettingsFormData(
                inGameplay: true,
                () =>
                {
                    pauseCount++;
                    return true;
                },
                () => resumeCount++);

            data.AcquireSettlementPause();
            data.AcquireSettlementPause();
            Assert.That(data.SettlementPauseOwned, Is.True);
            Assert.That(pauseCount, Is.EqualTo(1));

            data.ReleaseSettlementPause();
            data.ReleaseSettlementPause();
            Assert.That(data.SettlementPauseOwned, Is.False);
            Assert.That(resumeCount, Is.EqualTo(1));
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            if (field.FieldType.IsEnum && value is string enumName)
            {
                value = Enum.Parse(field.FieldType, enumName);
            }

            field.SetValue(target, value);
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
