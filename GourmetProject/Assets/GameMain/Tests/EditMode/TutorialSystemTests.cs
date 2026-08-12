using System.IO;
using System.Linq;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Game.Tutorial;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TutorialSystemTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name => JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void TutorialRunEligibility_IsConsumedOnlyOnce()
        {
            var progress = new GuideProgressSaveData();

            Assert.That(progress.TryConsumeCoreTutorialRun(), Is.True);
            Assert.That(progress.CoreTutorialRunConsumed, Is.True);
            Assert.That(progress.TryConsumeCoreTutorialRun(), Is.False);
        }

        [Test]
        public void ActionScheduleOverride_OnlyAppliesToTutorialRun()
        {
            GameRun tutorialRun = CreateRun(isTutorialRun: true);
            GameRun normalRun = CreateRun(isTutorialRun: false);

            Assert.That(
                TutorialActionScheduleOverride.TryBuildChoices(
                    tutorialRun,
                    coreCompleted: false,
                    out var tutorialChoices),
                Is.True);
            Assert.That(tutorialChoices.Select(choice => choice.Action.Id),
                Is.EqualTo(new[] { "act_food_gold" }));

            Assert.That(
                TutorialActionScheduleOverride.TryBuildChoices(
                    normalRun,
                    coreCompleted: false,
                    out var normalChoices),
                Is.False);
            Assert.That(normalChoices, Is.Null);
        }

        [Test]
        public void TutorialRunFlag_RoundTripsThroughRunSave()
        {
            GameRun original = CreateRun(isTutorialRun: true);

            RunSaveData save = original.ToSaveData();
            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(save.IsTutorialRun, Is.True);
            Assert.That(restored.IsTutorialRun, Is.True);
        }

        [Test]
        public void GuideProgress_Normalize_DeduplicatesAndDropsCompletedPending()
        {
            var progress = new GuideProgressSaveData
            {
                CompletedTutorialIds = new() { TutorialId.Flavor, string.Empty, TutorialId.Flavor },
                PendingTutorialIds = new() { TutorialId.Flavor, TutorialId.Material, TutorialId.Material, null },
            };

            progress.Normalize();

            Assert.That(progress.CompletedTutorialIds, Is.EqualTo(new[] { TutorialId.Flavor }));
            Assert.That(progress.PendingTutorialIds, Is.EqualTo(new[] { TutorialId.Material }));
        }

        [Test]
        public void HeartLoss_TruncatesAtZero_AndCompatibilityWrapperLosesOne()
        {
            GameRun twoHeartLoss = CreateRun();
            Assert.That(twoHeartLoss.TryLoseHearts(2, out int before, out int after), Is.True);
            Assert.That(before - after, Is.EqualTo(2));

            GameRun truncated = CreateRun();
            Assert.That(truncated.TryLoseHearts(99, out before, out after), Is.True);
            Assert.That(after, Is.Zero);

            GameRun wrapper = CreateRun();
            Assert.That(wrapper.TryLoseHeart(out before, out after), Is.True);
            Assert.That(before - after, Is.EqualTo(1));
        }

        [Test]
        public void TimedBusinessGoldMultiplier_AppliesPerBusinessAndSurvivesSave()
        {
            GameRun run = CreateRun();
            EffectResolver.Apply(run, cfg.EffectType.AddBusinessGoldPct, 0.2f, "Next", null);
            string feedback = EffectResolver.Apply(run, cfg.EffectType.AddBusinessGoldPct, 0.5f, "Next:2", null);

            Assert.That(feedback, Is.EqualTo("后续 2 次营业基础金币 ×1.5。"));

            Assert.That(run.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1.8f).Within(0.0001f));

            RunSaveData save = run.ToSaveData();
            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(restored.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(restored.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TimedBusinessGoldMultiplier_RejectsInvalidCountAndMigratesLegacySave()
        {
            GameRun run = CreateRun();
            string invalid = EffectResolver.Apply(run, cfg.EffectType.AddBusinessGoldPct, 0.5f, "Next:0", null);

            Assert.That(invalid, Does.Contain("参数无效"));
            Assert.That(run.CurrentWeekBusinessGoldMultiplier, Is.EqualTo(1f));
            Assert.That(run.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1f));

            string oversized = EffectResolver.Apply(run, cfg.EffectType.AddBusinessGoldPct, 0.5f, "Next:2147483647", null);
            Assert.That(oversized, Does.Contain("参数无效"));

            RunSaveData legacySave = run.ToSaveData();
            legacySave.NextBusinessGoldMultipliers = null;
            legacySave.NextBusinessGoldMultiplier = 1.6f;
            GameRun restored = GameRun.FromSaveData(_tables, _database, legacySave);

            Assert.That(restored.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1.6f).Within(0.0001f));
            Assert.That(restored.ConsumeNextBusinessGoldMultiplier(), Is.EqualTo(1f));
        }

        [Test]
        public void RestoreHeartsEffect_RestoresActualAmountAndStopsAtCapacity()
        {
            GameRun run = CreateRun();
            Assert.That(run.TryLoseHeart(out _, out _), Is.True);

            string restored = EffectResolver.Apply(run, cfg.EffectType.RestoreHearts, 1f, "-", null);
            Assert.That(run.HeartsRemaining, Is.EqualTo(run.HeartCapacity));
            Assert.That(restored, Is.EqualTo("恢复1颗红心。"));

            string full = EffectResolver.Apply(run, cfg.EffectType.RestoreHearts, 1f, "-", null);
            Assert.That(run.HeartsRemaining, Is.EqualTo(run.HeartCapacity));
            Assert.That(full, Is.EqualTo("红心已满。"));
        }

        [Test]
        public void TwinPeaksRewards_LoadWithImplementedEffects()
        {
            cfg.EventOption coffee = _tables.TbEventOption.Get("opt_twin_peaks_coffee_reward");
            Assert.That(coffee.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.AddBusinessGoldPct }));
            Assert.That(coffee.EffectValues.Single(), Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(coffee.EffectParams.Single(), Is.EqualTo("Next:2"));

            cfg.EventOption pie = _tables.TbEventOption.Get("opt_twin_peaks_pie_reward");
            Assert.That(pie.Text, Is.EqualTo("恢复1颗红心"));
            Assert.That(pie.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.RestoreHearts }));
            Assert.That(pie.EffectValues.Single(), Is.EqualTo(1f));

            cfg.EventOption log = _tables.TbEventOption.Get("opt_twin_peaks_log_reward");
            Assert.That(log.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.AddAllRecipeScoreFlat }));
            Assert.That(log.EffectValues.Single(), Is.EqualTo(10f));
        }

        [Test]
        public void ChukaIchibanRewards_LoadWithImplementedEffects()
        {
            cfg.GameEvent cookingTrial = _tables.TbEvent.Get("ev_chuka_ichiban_trial");
            Assert.That(cookingTrial.Name, Is.EqualTo("最后一灶也会发光"));

            cfg.EventOption friedRice = _tables.TbEventOption.Get("opt_chuka_ichiban_fried_rice_reward");
            Assert.That(friedRice.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.GainGold }));
            Assert.That(friedRice.EffectValues.Single(), Is.EqualTo(80f));

            cfg.EventOption mapoTofu = _tables.TbEventOption.Get("opt_chuka_ichiban_mapo_tofu_reward");
            Assert.That(mapoTofu.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.AddRandomRecipeFlavor }));
            Assert.That(mapoTofu.EffectValues.Single(), Is.EqualTo(1f));

            cfg.EventOption dumpling = _tables.TbEventOption.Get("opt_chuka_ichiban_dumpling_reward");
            Assert.That(dumpling.Text, Is.EqualTo("恢复1颗红心"));
            Assert.That(dumpling.EffectTypes, Is.EqualTo(new[] { cfg.EffectType.RestoreHearts }));
            Assert.That(dumpling.EffectValues.Single(), Is.EqualTo(1f));
        }

        [Test]
        public void ContentAcquired_ReportsMaterialFromTheUnifiedRunEvent()
        {
            GameRun run = CreateRun();
            RunContentAcquisition observed = null;
            run.ContentAcquired += acquisition => observed = acquisition;

            Assert.That(run.AddCellMaterial(new GridPos(0, 0), "material_test"), Is.True);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.Kind, Is.EqualTo(RunContentAcquisitionKind.TableMaterial));
            Assert.That(observed.MaterialId, Is.EqualTo("material_test"));
        }

        [Test]
        public void TutorialCatalog_ContainsEveryRequiredSequence()
        {
            string[] ids =
            {
                TutorialId.FirstAction, TutorialId.FirstBattle, TutorialId.Settlement,
                TutorialId.RewardSummary, TutorialId.SecondAction, TutorialId.TimelineNode,
                TutorialId.Flavor, TutorialId.Material, TutorialId.Adjustment,
                TutorialId.Boss, TutorialId.Failure, TutorialId.PassiveItem,
            };
            Assert.That(ids.All(id => TutorialCatalog.Get(id)?.Steps.Count > 0), Is.True);
        }

        private GameRun CreateRun(bool isTutorialRun = false) =>
            new(
                _tables,
                _database,
                "glutton_dog",
                "tutorial-test",
                weekIndex: 1,
                isTutorialRun: isTutorialRun);
    }
}
