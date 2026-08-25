#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Tutorial;
using GourmetProject.Gameplay.Data;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TutorialAcquisitionHookTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void ContentHookFor_OnlyClassifiesSuccessfullyAcquiredTutorialItems()
        {
            Assert.That(
                TutorialRuntime.ContentHookFor(new RunContentAcquisition
                {
                    Kind = RunContentAcquisitionKind.Item,
                    ItemKind = cfg.ItemKind.Passive,
                }),
                Is.EqualTo(TutorialId.PassiveItem));
            Assert.That(
                TutorialRuntime.ContentHookFor(new RunContentAcquisition
                {
                    Kind = RunContentAcquisitionKind.Item,
                    ItemKind = cfg.ItemKind.Active,
                    ItemEffectType = ItemEffectTypes.AddFlavor,
                }),
                Is.EqualTo(TutorialId.Flavor));
            Assert.That(
                TutorialRuntime.ContentHookFor(new RunContentAcquisition
                {
                    Kind = RunContentAcquisitionKind.Item,
                    ItemKind = cfg.ItemKind.Active,
                    ActiveItemCategory = cfg.ActiveItemCategory.Adjust,
                }),
                Is.EqualTo(TutorialId.Adjustment));

            Assert.That(
                TutorialRuntime.ContentHookFor(new RunContentAcquisition
                {
                    Kind = RunContentAcquisitionKind.DishFlavor,
                }),
                Is.Empty);
            Assert.That(
                TutorialRuntime.ContentHookFor(new RunContentAcquisition
                {
                    Kind = RunContentAcquisitionKind.Item,
                    ItemKind = cfg.ItemKind.Active,
                    ItemEffectType = ItemEffectTypes.EnhanceFlavor,
                }),
                Is.Empty);
            Assert.That(
                TutorialRuntime.ContentHookFor(new RunContentAcquisition
                {
                    Kind = RunContentAcquisitionKind.Item,
                    ItemKind = cfg.ItemKind.Active,
                    ActiveItemCategory = cfg.ActiveItemCategory.Strengthen,
                    ItemEffectType = ItemEffectTypes.GoldNow,
                }),
                Is.Empty);
        }

        [Test]
        public void TutorialCatalog_UsesExpectedAcquiredItemCopyAndAnchors()
        {
            TutorialSequenceDefinition passive = TutorialCatalog.Get(TutorialId.PassiveItem);
            Assert.That(passive.Steps.Count, Is.EqualTo(1));
            Assert.That(
                passive.Steps[0].Message,
                Is.EqualTo("装饰品获得后会永久生效。这可是我们提升餐厅实力的重要方面呢！"));
            Assert.That(
                passive.Steps[0].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.AcquiredPassiveItem }));

            TutorialSequenceDefinition adjustment = TutorialCatalog.Get(TutorialId.Adjustment);
            Assert.That(adjustment.Steps.Count, Is.EqualTo(1));
            Assert.That(adjustment.Steps[0].Message, Is.EqualTo("这是调整单！它可以修改节点和行动。"));
            Assert.That(
                adjustment.Steps[0].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.AcquiredActiveItem }));

            TutorialSequenceDefinition flavor = TutorialCatalog.Get(TutorialId.Flavor);
            Assert.That(flavor.Steps.Count, Is.EqualTo(2));
            Assert.That(
                flavor.Steps[0].Message,
                Is.EqualTo("老板，这是风味罐，可以帮我们为食物附加风味。"));
            Assert.That(
                flavor.Steps[1].Message,
                Is.EqualTo("风味会改变食物的属性和结算效果，每个食物只有 1 个风味位哦。"));
            Assert.That(
                flavor.Steps[0].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.AcquiredActiveItem }));
            Assert.That(
                flavor.Steps[1].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.AcquiredActiveItem }));
        }

        [Test]
        public void AcquireItem_NotifiesOnlyAfterAnItemIsStored()
        {
            GameRun run = CreateRun();
            var acquisitions = new List<RunContentAcquisition>();
            run.ContentAcquired += acquisitions.Add;

            ItemAcquireResult passive = run.AcquireItem("item_gold_boss", fallbackGold: 0);
            ItemAcquireResult flavor = run.AcquireItem("item_active_season_sweet", fallbackGold: 0);
            ItemAcquireResult adjustment = run.AcquireItem("item_active_half_next_action_cost", fallbackGold: 0);

            Assert.That(passive.Outcome, Is.EqualTo(ItemAcquireOutcome.Added));
            Assert.That(flavor.Outcome, Is.EqualTo(ItemAcquireOutcome.Stacked));
            Assert.That(adjustment.Outcome, Is.EqualTo(ItemAcquireOutcome.Stacked));
            Assert.That(acquisitions.Count, Is.EqualTo(3));
            Assert.That(TutorialRuntime.ContentHookFor(acquisitions[0]), Is.EqualTo(TutorialId.PassiveItem));
            Assert.That(TutorialRuntime.ContentHookFor(acquisitions[1]), Is.EqualTo(TutorialId.Flavor));
            Assert.That(TutorialRuntime.ContentHookFor(acquisitions[2]), Is.EqualTo(TutorialId.Adjustment));

            ItemAcquireResult duplicatePassive = run.AcquireItem("item_gold_boss", fallbackGold: 1);
            Assert.That(duplicatePassive.Outcome, Is.EqualTo(ItemAcquireOutcome.ConvertedToGold));
            Assert.That(acquisitions.Count, Is.EqualTo(3));

            run.AcquireItem("item_active_season_sweet", fallbackGold: 0);
            run.AcquireItem("item_active_season_sweet", fallbackGold: 0);
            int beforeOverflow = acquisitions.Count;
            ItemAcquireResult overflow = run.AcquireItem("item_active_season_sweet", fallbackGold: 1);
            Assert.That(overflow.Outcome, Is.EqualTo(ItemAcquireOutcome.ConvertedToGold));
            Assert.That(acquisitions.Count, Is.EqualTo(beforeOverflow));

            GameRun silentRun = CreateRun();
            int silentNotifications = 0;
            silentRun.ContentAcquired += _ => silentNotifications++;
            silentRun.AcquireItem("item_gold_boss", fallbackGold: 0, fireOnAcquire: false);
            silentRun.AcquireItem("item_active_season_sweet", fallbackGold: 0, fireOnAcquire: false);
            Assert.That(silentNotifications, Is.Zero);
        }

        [Test]
        public void PresentationGate_NotifiesOnlyAfterTheItemArrives()
        {
            var presented = new List<RunContentAcquisition>();
            var gate = new TutorialAcquiredItemPresentationGate(presented.Add);
            var acquisition = new RunContentAcquisition
            {
                Kind = RunContentAcquisitionKind.Item,
                ItemId = "item_active_half_next_action_cost",
                ItemKind = cfg.ItemKind.Active,
                ActiveItemCategory = cfg.ActiveItemCategory.Adjust,
            };

            gate.Stage(acquisition);

            Assert.That(gate.HasPending, Is.True);
            Assert.That(presented, Is.Empty, "获得事件只能暂存，飞行动画抵达前不能播放教程。");

            gate.Complete();
            gate.Complete();

            Assert.That(gate.HasPending, Is.False);
            Assert.That(presented, Is.EqualTo(new[] { acquisition }));
        }

        private GameRun CreateRun() =>
            new(
                _tables,
                _database,
                "glutton_dog",
                "tutorial-acquisition-hook-tests",
                weekIndex: 1,
                isTutorialRun: false);
    }
}
#endif
