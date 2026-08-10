using System.IO;
using System.Linq;
using GourmetProject.Game.Adapter;
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
