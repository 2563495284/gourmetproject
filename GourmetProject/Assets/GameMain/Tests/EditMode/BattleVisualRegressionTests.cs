using System;
using System.Reflection;
using System.Threading;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
        public void DishValueBadge_SetValueImmediatelyRefreshesTextMesh()
        {
            const string path =
                "Assets/GameMain/Content/Prefabs/Battle/DishValueBadge.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            try
            {
                DishValueBadgeView badge = instance.GetComponent<DishValueBadgeView>();
                TextMeshPro valueText = instance.GetComponentInChildren<TextMeshPro>(true);
                Assert.That(badge, Is.Not.Null);
                Assert.That(valueText, Is.Not.Null);

                valueText.text = "20";
                valueText.ForceMeshUpdate(true, true);
                badge.SetValue("300");

                Assert.That(valueText.textInfo.characterCount, Is.EqualTo(3));
                Assert.That(valueText.textInfo.characterInfo[0].character, Is.EqualTo('3'));
                Assert.That(valueText.textInfo.characterInfo[1].character, Is.EqualTo('0'));
                Assert.That(valueText.textInfo.characterInfo[2].character, Is.EqualTo('0'));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DishIconPreview_SequentialValuesRenderDistinctBadges()
        {
            const string badgePath =
                "Assets/GameMain/Content/Prefabs/Battle/DishValueBadge.prefab";
            GameObject badgePrefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(badgePath);
            Assert.That(badgePrefabObject, Is.Not.Null);
            DishValueBadgeView badgePrefab = badgePrefabObject.GetComponent<DishValueBadgeView>();
            Assert.That(badgePrefab, Is.Not.Null);

            var sourceTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[16 * 16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(242, 224, 190, 255);
            }
            sourceTexture.SetPixels32(pixels);
            sourceTexture.Apply();
            Sprite sprite = Sprite.Create(
                sourceTexture,
                new Rect(0f, 0f, 16f, 16f),
                new Vector2(0.5f, 0.5f),
                16f);
            var cellObject = new GameObject("CellPrefab");
            SpriteRenderer cell = cellObject.AddComponent<SpriteRenderer>();
            cell.sprite = sprite;
            var dish = new DishDef(
                "rt_badge_test",
                "RT Badge Test",
                20,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
            RenderTexture first = null;
            RenderTexture second = null;
            RenderTexture third = null;
            Texture2D readback = null;

            try
            {
                DestroyPreviewRigs();
                first = DishIconPreviewRenderer.Render(
                    dish, sprite, 20, null, cell, badgePrefab, 96, DishIconPreviewMode.Card);
                second = DishIconPreviewRenderer.Render(
                    dish, sprite, 75, null, cell, badgePrefab, 96, DishIconPreviewMode.Card);
                third = DishIconPreviewRenderer.Render(
                    dish, sprite, 300, null, cell, badgePrefab, 96, DishIconPreviewMode.Card);

                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.Not.Null);
                Assert.That(third, Is.Not.Null);
                readback = new Texture2D(96, 96, TextureFormat.RGBA32, false);
                long firstChecksum = ReadbackChecksum(first, readback);
                long secondChecksum = ReadbackChecksum(second, readback);
                long thirdChecksum = ReadbackChecksum(third, readback);

                Assert.That(secondChecksum, Is.Not.EqualTo(firstChecksum),
                    "连续候选预览不能沿用第一张卡的美味值。");
                Assert.That(thirdChecksum, Is.Not.EqualTo(firstChecksum),
                    "300 分候选不能显示成第一张卡的美味值。");
                Assert.That(thirdChecksum, Is.Not.EqualTo(secondChecksum),
                    "每张候选卡必须渲染自己的真实美味值。");
            }
            finally
            {
                ReleaseImmediate(first);
                ReleaseImmediate(second);
                ReleaseImmediate(third);
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                UnityEngine.Object.DestroyImmediate(cellObject);
                DestroyPreviewRigs();
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

        private static long ReadbackChecksum(RenderTexture source, Texture2D target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = source;
            target.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            target.Apply(false, false);
            RenderTexture.active = previous;

            Color32[] pixels = target.GetPixels32();
            long checksum = 17;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                checksum = unchecked(checksum * 31 + pixel.r);
                checksum = unchecked(checksum * 31 + pixel.g);
                checksum = unchecked(checksum * 31 + pixel.b);
                checksum = unchecked(checksum * 31 + pixel.a);
            }
            return checksum;
        }

        private static void ReleaseImmediate(RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void DestroyPreviewRigs()
        {
            foreach (DishIconPreviewRenderer rig in Resources.FindObjectsOfTypeAll<DishIconPreviewRenderer>())
            {
                if (rig != null)
                {
                    UnityEngine.Object.DestroyImmediate(rig.gameObject);
                }
            }
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

            Assert.That(label, Is.EqualTo("永久 +3"));
            Assert.That(permanentColor, Is.Not.EqualTo(temporaryColor));
            Assert.That(permanentColor, Is.Not.EqualTo(SettlementColorPalette.AddMultiplier));
            Assert.That(permanentColor, Is.Not.EqualTo(SettlementColorPalette.MultiplyMultiplier));
            AssertColor(permanentColor, 184, 90, 43);
        }

        [TestCase(ScoreLineKind.DishBase, 1, SettlementImpactTier.Base)]
        [TestCase(ScoreLineKind.DishFlat, 1, SettlementImpactTier.Normal)]
        [TestCase(ScoreLineKind.DishFlat, 3, SettlementImpactTier.Strong)]
        [TestCase(ScoreLineKind.DishMultiplier, 1, SettlementImpactTier.Strong)]
        [TestCase(ScoreLineKind.DishMultiplier, 3, SettlementImpactTier.Chain)]
        [TestCase(ScoreLineKind.CopySkill, 1, SettlementImpactTier.Chain)]
        public void SettlementImpact_ClimbsForMultipliersChainsAndMultipleTargets(
            ScoreLineKind kind,
            int targetCount,
            SettlementImpactTier expected)
        {
            ScoreLine line = ScoreLineForImpact(kind);

            Assert.That(SettlementSequencer.ImpactFor(line, targetCount), Is.EqualTo(expected));
        }

        [Test]
        public void SettlementBeat_CarriesExactScoreDeltaAndTargetCrossing()
        {
            var signal = new SettlementBeatSignal(
                SettlementBeatKind.ResultApplied,
                "测试来源",
                12,
                1.2f,
                0.5f,
                ScoreLineKind.DishMultiplier,
                90f,
                135f,
                SettlementImpactTier.Strong,
                2,
                reachedTarget: true);

            Assert.That(signal.HasScoreChange, Is.True);
            Assert.That(signal.ScoreDelta.ToDouble(), Is.EqualTo(45d));
            Assert.That(signal.TargetCount, Is.EqualTo(2));
            Assert.That(signal.ReachedTarget, Is.True);
        }

        [TestCase(99d, 100, 0)]
        [TestCase(100d, 100, 1)]
        [TestCase(150d, 100, 1)]
        [TestCase(200d, 100, 2)]
        [TestCase(250d, 100, 2)]
        [TestCase(250d, 0, 0)]
        public void SettlementPace_ResolvesFromVisibleScoreThresholds(
            double score,
            int requiredScore,
            int expected)
        {
            Assert.That(
                (int)SettlementSequencer.ResolvePacePhase(score, requiredScore),
                Is.EqualTo(expected));
        }

        [Test]
        public void SettlementPace_NeverDropsAndCanSkipDirectlyToDoubleTarget()
        {
            Assert.That(
                SettlementSequencer.ResolvePacePhase(
                    20d,
                    100,
                    SettlementPacePhase.TargetReached),
                Is.EqualTo(SettlementPacePhase.TargetReached));
            Assert.That(
                SettlementSequencer.ResolvePacePhase(
                    240d,
                    100,
                    SettlementPacePhase.BelowTarget),
                Is.EqualTo(SettlementPacePhase.DoubleTarget));
        }

        [TestCase(0, false, 1f)]
        [TestCase(1, false, 1.4f)]
        [TestCase(2, false, 1.8f)]
        [TestCase(0, true, 2f)]
        [TestCase(1, true, 2.8f)]
        [TestCase(2, true, 3.6f)]
        public void SettlementPace_MapsToExactConfiguredSpeed(
            int phase,
            bool doubleSpeed,
            float expected)
        {
            Assert.That(
                SettlementSequencer.DefaultSpeedForPhase(
                    (SettlementPacePhase)phase,
                    doubleSpeed),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void SettlementPace_ThresholdCueUsesOldSpeedAndNextCueUsesPromotedSpeed()
        {
            SettlementPacePhase phase = SettlementPacePhase.BelowTarget;
            float thresholdCueSpeed = SettlementSequencer.DefaultSpeedForPhase(phase, false);
            SettlementPacePhase promoted = SettlementSequencer.ResolvePacePhase(100d, 100, phase);
            float nextCueSpeed = SettlementSequencer.DefaultSpeedForPhase(promoted, false);

            Assert.That(thresholdCueSpeed, Is.EqualTo(1f));
            Assert.That(promoted, Is.EqualTo(SettlementPacePhase.TargetReached));
            Assert.That(nextCueSpeed, Is.EqualTo(1.4f));
        }

        [Test]
        public void SettlementScoreFire_UsesCanvasParticleGraphicsAndStaysHiddenBeforeTarget()
        {
            const string path = "Assets/GameMain/Content/Prefabs/UI/BattleForm.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            try
            {
                SettlementScoreFireView fire = instance.GetComponentInChildren<SettlementScoreFireView>(true);
                RectTransform score = Array.Find(
                    instance.GetComponentsInChildren<RectTransform>(true),
                    rect => rect.name == "CurrentScoreText");
                Assert.That(fire, Is.Not.Null);
                Assert.That(score, Is.Not.Null);

                fire.BindToScore(score);
                fire.Show();
                fire.SetPhase(SettlementPacePhase.BelowTarget, 1f);
                fire.Burst(1f);
                Assert.That(fire.ActiveMoteCount, Is.Zero);
                Assert.That(fire.CoreEmissionRate, Is.Zero);
                Assert.That(fire.TongueEmissionRate, Is.Zero);
                Assert.That(fire.EmberEmissionRate, Is.Zero);

                fire.SetPhase(SettlementPacePhase.TargetReached, 1.4f);
                fire.Burst(1f);
                Assert.That(fire.ActiveMoteCount, Is.GreaterThan(0));
                Assert.That(fire.EffectiveSimulationSpeed, Is.EqualTo(1.4f).Within(0.0001f));
                Assert.That(fire.CoreEmissionRate, Is.EqualTo(30f));
                Assert.That(fire.TongueEmissionRate, Is.EqualTo(16f));
                Assert.That(fire.EmberEmissionRate, Is.EqualTo(2f));
                Assert.That(fire.CoreEmitterRunning, Is.True);
                Assert.That(fire.TongueEmitterRunning, Is.True);
                Assert.That(fire.EmberEmitterRunning, Is.True);
                Assert.That(fire.transform.parent, Is.SameAs(score.parent));
                var fireRect = (RectTransform)fire.transform;
                Assert.That(fireRect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(fireRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(fireRect.GetSiblingIndex(), Is.Zero,
                    "火焰必须是 ScoreMeter 中三行分数文本的背景层。");

                SettlementUiParticleGraphic[] graphics =
                    fire.GetComponentsInChildren<SettlementUiParticleGraphic>(true);
                Assert.That(graphics.Length, Is.EqualTo(2),
                    "辉光和火焰主体应分别批绘，最多增加两个 Canvas draw call。");
                Assert.That(Array.TrueForAll(graphics, graphic => !graphic.raycastTarget), Is.True);
                Assert.That(Array.TrueForAll(
                    graphics,
                    graphic => graphic.material != null
                        && graphic.material.shader.name == "GourmetProject/SettlementUiParticle"), Is.True);

                ParticleSystem[] systems = fire.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(systems.Length, Is.EqualTo(3));
                Assert.That(Array.TrueForAll(
                    systems,
                    system => system.GetComponent<ParticleSystemRenderer>() == null
                        || !system.GetComponent<ParticleSystemRenderer>().enabled), Is.True,
                    "标准 ParticleSystemRenderer 在 Overlay Canvas 中必须关闭，由 UGUI Graphic 绘制。");

                fire.SetPhase(SettlementPacePhase.DoubleTarget, 3.6f);
                Assert.That(fire.EffectiveSimulationSpeed, Is.EqualTo(3.6f).Within(0.0001f));
                Assert.That(fire.CoreEmissionRate, Is.EqualTo(46f));
                Assert.That(fire.TongueEmissionRate, Is.EqualTo(27f));
                Assert.That(fire.EmberEmissionRate, Is.EqualTo(6f));

                fire.Hide();
                Assert.That(fire.ActiveMoteCount, Is.Zero);
                Assert.That(fire.CoreEmitterRunning, Is.False);
                Assert.That(fire.TongueEmitterRunning, Is.False);
                Assert.That(fire.EmberEmitterRunning, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
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

        private static ScoreLine ScoreLineForImpact(ScoreLineKind kind)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                ScoreSource.FinalModifier("impact_test", "冲击测试"),
                1,
                "dish",
                null,
                2f,
                0f,
                2f,
                string.Empty);
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
