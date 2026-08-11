using System;
using System.Reflection;
using System.Threading;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Meta;
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
        public void PendingDishVisualCommit_DoesNotPublishTransientOutletState()
        {
            var root = new GameObject("PendingDishVisualCommitTest");
            BattleWorldController controller = root.AddComponent<BattleWorldController>();
            int stateChangedCount = 0;

            try
            {
                FieldInfo field = typeof(BattleWorldController).GetField(
                    "_stateChanged",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                field.SetValue(controller, new Action(() => stateChangedCount++));

                var dish = new DishDef(
                    "dish_outlet_transition",
                    "出餐口切换测试菜",
                    1,
                    DishShape.FromRows(new[] { "X" }),
                    0,
                    0,
                    1f,
                    Array.Empty<string>(),
                    string.Empty,
                    allowRotate: false);
                var instance = new DishInstance(
                    1,
                    dish,
                    new Placement(dish.Shape, 0, new GridPos(0, 0)),
                    Array.Empty<string>(),
                    Array.Empty<string>());
                var result = new PendingDishConfirmResult(
                    true,
                    instance,
                    PendingDishActionKind.Serve);

                _ = controller.CommitPendingDishVisualStateAsync(
                    result,
                    CancellationToken.None);

                Assert.That(
                    stateChangedCount,
                    Is.Zero,
                    "中间演出不得刷新 HUD；下一道菜应在最终刷新前准备好。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
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
        public void PermanentFlat_UsesDistinctLabelAndColorFromTemporaryFlat()
        {
            var permanent = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.DishPermanentFlat,
                ScoreSource.FinalModifier("permanent", "永久加分"),
                1,
                "dish",
                null,
                3f,
                0f,
                3f,
                string.Empty);

            string label = SettlementStageView.ResultText(permanent, 13f, 13f);
            Color temporaryColor = SettlementColorPalette.For(ScoreLineKind.DishFlat);
            Color permanentColor = SettlementColorPalette.For(ScoreLineKind.DishPermanentFlat);

            Assert.That(label, Is.EqualTo("永久分数 +3"));
            Assert.That(permanentColor, Is.Not.EqualTo(temporaryColor));
            Assert.That(permanentColor, Is.Not.EqualTo(SettlementColorPalette.AddMultiplier));
            Assert.That(permanentColor, Is.Not.EqualTo(SettlementColorPalette.MultiplyMultiplier));
            AssertColor(permanentColor, 184, 90, 43);
        }

        [TestCase(ScoreLineKind.DishBase, 246, 196, 83)]
        [TestCase(ScoreLineKind.DishFlat, 246, 196, 83)]
        [TestCase(ScoreLineKind.FinalFlat, 246, 196, 83)]
        [TestCase(ScoreLineKind.DishPermanentFlat, 184, 90, 43)]
        [TestCase(ScoreLineKind.DishMultiplierAdd, 57, 208, 176)]
        [TestCase(ScoreLineKind.DishMultiplier, 255, 90, 95)]
        [TestCase(ScoreLineKind.FinalMultiplier, 255, 90, 95)]
        [TestCase(ScoreLineKind.Gold, 244, 183, 64)]
        [TestCase(ScoreLineKind.Layer, 255, 138, 61)]
        [TestCase(ScoreLineKind.SilverItemRoll, 169, 196, 216)]
        [TestCase(ScoreLineKind.CopySkill, 54, 224, 242)]
        [TestCase(ScoreLineKind.TriggerSweetTransfer, 255, 84, 178)]
        [TestCase(ScoreLineKind.TriggeredSweetTransferSource, 255, 84, 178)]
        [TestCase(ScoreLineKind.SweetTransferBuffApplied, 255, 84, 178)]
        [TestCase(ScoreLineKind.SweetTransferBuffTriggered, 255, 84, 178)]
        [TestCase(ScoreLineKind.SweetTransferFailed, 224, 106, 132)]
        public void SettlementPalette_MapsEveryScoreLineKindToItsSemanticColor(
            ScoreLineKind kind,
            int expectedR,
            int expectedG,
            int expectedB)
        {
            AssertColor(SettlementColorPalette.For(kind), expectedR, expectedG, expectedB);
        }

        [TestCase(ScoreLineKind.SweetTransferBuffApplied)]
        [TestCase(ScoreLineKind.SweetTransferBuffTriggered)]
        [TestCase(ScoreLineKind.SweetTransferFailed)]
        public void SettlementResultTheme_UsesSemanticSideEffectColor(ScoreLineKind kind)
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                ScoreSource.FinalModifier("side_effect", "副作用"),
                1,
                "dish",
                null,
                1f,
                0f,
                1f,
                string.Empty);

            Assert.That(SettlementStageView.ResultThemeFor(line), Is.EqualTo(SettlementColorPalette.For(kind)));
        }

        [TestCase(ScoreLineKind.DishFlat)]
        [TestCase(ScoreLineKind.DishPermanentFlat)]
        [TestCase(ScoreLineKind.DishMultiplierAdd)]
        [TestCase(ScoreLineKind.DishMultiplier)]
        [TestCase(ScoreLineKind.Gold)]
        [TestCase(ScoreLineKind.Layer)]
        [TestCase(ScoreLineKind.SilverItemRoll)]
        [TestCase(ScoreLineKind.CopySkill)]
        [TestCase(ScoreLineKind.TriggerSweetTransfer)]
        [TestCase(ScoreLineKind.SweetTransferFailed)]
        public void SettlementPalette_UsesBrightSemanticTextOnDarkSemanticPlate(ScoreLineKind kind)
        {
            Color theme = SettlementColorPalette.For(kind);
            Color plate = SettlementColorPalette.PlateFor(theme);
            Color text = SettlementColorPalette.TextFor(theme);

            Assert.That(RelativeLuminance(text), Is.GreaterThan(RelativeLuminance(plate)));
            Assert.That(ContrastRatio(text, plate), Is.GreaterThanOrEqualTo(4.5f));
            Assert.That((Color32)text, Is.Not.EqualTo((Color32)SettlementColorPalette.TextInk));
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

        private static void AssertColor(Color color, int expectedR, int expectedG, int expectedB)
        {
            Color32 actual = color;
            Assert.That(actual.r, Is.EqualTo(expectedR));
            Assert.That(actual.g, Is.EqualTo(expectedG));
            Assert.That(actual.b, Is.EqualTo(expectedB));
            Assert.That(actual.a, Is.EqualTo(255));
        }

        private static float ContrastRatio(Color first, Color second)
        {
            float bright = Mathf.Max(RelativeLuminance(first), RelativeLuminance(second));
            float dark = Mathf.Min(RelativeLuminance(first), RelativeLuminance(second));
            return (bright + 0.05f) / (dark + 0.05f);
        }

        private static float RelativeLuminance(Color color)
        {
            return 0.2126f * LinearChannel(color.r)
                + 0.7152f * LinearChannel(color.g)
                + 0.0722f * LinearChannel(color.b);
        }

        private static float LinearChannel(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }
    }
}
