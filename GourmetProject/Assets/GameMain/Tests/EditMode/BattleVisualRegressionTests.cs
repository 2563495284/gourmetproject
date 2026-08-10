using System;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleVisualRegressionTests
    {
        [Test]
        public void CannotPlaceFrame_RemainsRedWhenIdleAndHovered()
        {
            var root = new GameObject(
                "CannotPlaceFrame",
                typeof(RectTransform),
                typeof(CanvasRenderer));
            var frame = root.AddComponent<RecipeWarehouseItemFrameGraphic>();

            try
            {
                frame.Configure(highlighted: false, clickable: true, cannotPlace: true);
                Color idleBorder = frame.BorderColor;
                Color idleFill = frame.FillColor;

                frame.Configure(highlighted: true, clickable: true, cannotPlace: true);
                Color hoverBorder = frame.BorderColor;
                Color hoverFill = frame.FillColor;

                AssertRedTheme(idleBorder);
                AssertRedTheme(hoverBorder);
                AssertRedTheme(idleFill);
                AssertRedTheme(hoverFill);
                Assert.That(idleBorder.a, Is.GreaterThanOrEqualTo(0.9f));
                Assert.That(hoverBorder.a, Is.GreaterThanOrEqualTo(idleBorder.a));
                Assert.That(idleFill.a, Is.GreaterThan(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DishValueBadge_DimmingHalvesAndRestoresOriginalAlpha()
        {
            const string path =
                "Assets/GameMain/Content/Prefabs/Battle/DishValueBadge.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            try
            {
                DishValueBadgeView badge = instance.GetComponent<DishValueBadgeView>();
                Assert.That(badge, Is.Not.Null);
                float originalAlpha = badge.CurrentAlpha;

                badge.SetDimmed(true);
                Assert.That(badge.CurrentAlpha, Is.EqualTo(originalAlpha * 0.5f).Within(0.0001f));

                badge.SetDimmed(false);
                Assert.That(badge.CurrentAlpha, Is.EqualTo(originalAlpha).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void PreparedServe_AlreadyContainsPermanentRecipeScoreModifiers()
        {
            var dish = new DishDef(
                "dish_test",
                "测试食物",
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
            var database = new GameplayDatabase(
                new[] { dish },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var entry = new RecipeSlotEntry(
                dish.Id,
                extraFlavorIds: null,
                extraSkillIds: null,
                scoreMultiplier: 1.5f,
                scoreFlatBonus: 4f);
            var session = new BattleSession(
                new DiningTable(1, 1),
                database,
                new Xoshiro256SS(7UL),
                new[] { new RecipeSlot("recipe_test", new[] { entry }) },
                requiredScore: 0);

            ServePrepareResult prepared = session.PrepareServe(0);

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.PreparedDish.Dish.PermanentFlatBonus.ToDouble(), Is.EqualTo(4d));
            Assert.That(prepared.PreparedDish.Dish.PermanentMultBonus.ToDouble(), Is.EqualTo(1.5d));

            ServeResult preplaced = session.PreplacePreparedServe(
                prepared.PreparedDish.Placements[0]);
            Assert.That(preplaced.Outcome, Is.EqualTo(ServeOutcome.Placed));
            Assert.That(preplaced.Dish.PermanentFlatBonus.ToDouble(), Is.EqualTo(4d));
            Assert.That(preplaced.Dish.PermanentMultBonus.ToDouble(), Is.EqualTo(1.5d));
        }

        [Test]
        public void TastingDebuff_UsesRedForBothPenaltyAndGainValues()
        {
            ServeTriggerCue penalty = Cue(
                ServeCueSourceKind.BossDebuff,
                "debuff_tasting",
                0.5f,
                ServeCuePresentationKind.Penalty);
            ServeTriggerCue gain = Cue(
                ServeCueSourceKind.BossDebuff,
                "debuff_tasting",
                1.5f,
                ServeCuePresentationKind.Gain);
            ServeTriggerCue passiveGain = Cue(
                ServeCueSourceKind.PassiveItem,
                "passive_test",
                1.5f,
                ServeCuePresentationKind.Gain);

            Color penaltyColor = BattleWorldController.ServeTriggerCueColor(penalty);
            Color gainColor = BattleWorldController.ServeTriggerCueColor(gain);
            Color passiveColor = BattleWorldController.ServeTriggerCueColor(passiveGain);

            Assert.That(gainColor, Is.EqualTo(penaltyColor));
            AssertRedTheme(gainColor);
            Assert.That(passiveColor, Is.Not.EqualTo(gainColor));
            Assert.That(passiveColor.g, Is.GreaterThan(passiveColor.b));
        }

        [Test]
        public void DiningTableVisualScale_TracksCellSizeAndCapsAtOne()
        {
            Assert.That(
                DiningTableLayout.VisualScaleForCellSize(DiningTableLayout.MaxCellSize),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                DiningTableLayout.VisualScaleForCellSize(DiningTableLayout.MaxCellSize * 0.5f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                DiningTableLayout.VisualScaleForCellSize(DiningTableLayout.MaxCellSize * 2f),
                Is.EqualTo(1f).Within(0.0001f));
        }

        [TestCase(SkillActionType.TransferSkills, SkillScope.Other)]
        [TestCase(SkillActionType.TransferSkills, SkillScope.Row)]
        [TestCase(SkillActionType.AddFlat, SkillScope.All)]
        [TestCase(SkillActionType.AddFlat, SkillScope.Other)]
        [TestCase(SkillActionType.AddLayer, SkillScope.CakeBuff)]
        public void ScopeHighlight_GlobalTargetsDoNotDrawTableRegion(
            SkillActionType actionType,
            SkillScope actionScope)
        {
            Assert.That(
                BattleScopeHighlightController.ShouldRenderTargetRegion(actionType, actionScope),
                Is.False);
        }

        [TestCase(SkillScope.Self)]
        [TestCase(SkillScope.Row)]
        [TestCase(SkillScope.Column)]
        [TestCase(SkillScope.Category)]
        public void ScopeHighlight_LocalTargetsKeepTheirRegion(SkillScope actionScope)
        {
            Assert.That(
                BattleScopeHighlightController.ShouldRenderTargetRegion(
                    SkillActionType.AddFlat,
                    actionScope),
                Is.True);
        }

        private static ServeTriggerCue Cue(
            ServeCueSourceKind sourceKind,
            string sourceId,
            float value,
            ServeCuePresentationKind presentationKind)
        {
            return new ServeTriggerCue(
                sourceKind,
                sourceId,
                sourceId,
                dishId: 1,
                ServeCueEffectKind.MultiplierFactor,
                value,
                $"×{value:0.0}",
                presentationKind);
        }

        private static void AssertRedTheme(Color color)
        {
            Assert.That(color.r, Is.GreaterThan(color.g));
            Assert.That(color.r, Is.GreaterThan(color.b));
        }
    }
}
