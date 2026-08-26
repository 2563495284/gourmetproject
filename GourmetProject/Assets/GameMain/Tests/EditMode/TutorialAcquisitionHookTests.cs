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
        public void TutorialCatalog_UsesCondensedCoreCopyAndInteractions()
        {
            TutorialSequenceDefinition firstAction = TutorialCatalog.Get(TutorialId.FirstAction);
            Assert.That(firstAction.Steps.Count, Is.EqualTo(2));
            Assert.That(
                new[] { firstAction.Steps[0].Message, firstAction.Steps[1].Message },
                Is.EqualTo(new[]
                {
                    "这是行动卡，行动会消耗时间，带来收益。",
                    "卡片上的图标，表示行动的额外奖励，点击开始营业吧！",
                }));
            Assert.That(firstAction.Steps[0].Mode, Is.EqualTo(TutorialAdvanceMode.Continue));
            Assert.That(firstAction.Steps[1].Mode, Is.EqualTo(TutorialAdvanceMode.Signal));
            Assert.That(firstAction.Steps[1].Signal, Is.EqualTo(TutorialSignal.ActionPicked));
            Assert.That(firstAction.Steps[1].AllowTargetInteraction, Is.True);
            Assert.That(
                firstAction.Steps[1].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.ActionCard0 }));

            TutorialSequenceDefinition firstBattle = TutorialCatalog.Get(TutorialId.FirstBattle);
            Assert.That(firstBattle.Steps.Count, Is.EqualTo(6));
            Assert.That(
                new[]
                {
                    firstBattle.Steps[0].Message,
                    firstBattle.Steps[1].Message,
                    firstBattle.Steps[2].Message,
                    firstBattle.Steps[3].Message,
                    firstBattle.Steps[4].Message,
                    firstBattle.Steps[5].Message,
                },
                Is.EqualTo(new[]
                {
                    "偷偷告诉老板营业的秘诀，就是把尽可能多的食物摆上餐桌。",
                    "老板，看这里！食物会从食谱中抽取。",
                    "你可以把它拖到餐桌上，也可以拖进垃圾桶丢弃",
                    "这里是现在的食谱，后续获得的食物也可以从这里查看。",
                    "每个食物都有自己的特殊效果。老板好好搭配，它们就能发挥更大的作用！",
                    "这里是本次营业需要达到的美味值，努力超过它吧！",
                }));
            Assert.That(
                firstBattle.Steps[2].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.Table, TutorialAnchorId.Discard }));
            Assert.That(firstBattle.Steps[3].EnterCommand, Is.EqualTo(TutorialCommand.OpenInitialRecipe));
            Assert.That(firstBattle.Steps[3].ExitCommand, Is.EqualTo(TutorialCommand.CloseInitialRecipe));
            Assert.That(
                firstBattle.Steps[3].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.RecipePanel }));
            Assert.That(firstBattle.Steps[4].EnterCommand, Is.EqualTo(TutorialCommand.ShowPreparedFoodTips));
            Assert.That(firstBattle.Steps[4].ExitCommand, Is.EqualTo(TutorialCommand.HidePreparedFoodTips));

            TutorialSequenceDefinition timeline = TutorialCatalog.Get(TutorialId.TimelineNode);
            Assert.That(timeline.Steps.Count, Is.EqualTo(1));
            Assert.That(
                timeline.Steps[0].Message,
                Is.EqualTo("普通行动推进时间轴时，经过的节点会依次触发。"));
            Assert.That(timeline.Steps[0].Mode, Is.EqualTo(TutorialAdvanceMode.Continue));
            Assert.That(timeline.Steps[0].Signal, Is.Empty);
            Assert.That(timeline.Steps[0].AllowTargetInteraction, Is.False);
            Assert.That(
                timeline.Steps[0].Anchors,
                Is.EqualTo(new[] { TutorialAnchorId.ActionAxis }));
        }

        [Test]
        public void TutorialCatalog_UsesCondensedSettlementAndFailureCopy()
        {
            Assert.That(
                TutorialCatalog.Get(TutorialId.FirstBattleSettleHint).Steps[0].Message,
                Is.EqualTo("等你准备好了，点击「结算」就可以完成本次经营！"));
            Assert.That(
                TutorialCatalog.Get(TutorialId.FirstBattleSettlementOrderHint).Steps[0].Message,
                Is.EqualTo("食物会从上到下，从左到右开始结算。合理摆放位置可以发挥更大的作用！"));
            Assert.That(
                TutorialCatalog.Get(TutorialId.Settlement).Steps[0].Message,
                Is.EqualTo("这是本次营业的结果。总美味值达到目标即为成功，否则营业失败。"));

            TutorialSequenceDefinition failure = TutorialCatalog.Get(TutorialId.FirstFailureHeart);
            Assert.That(failure.Steps.Count, Is.EqualTo(2));
            Assert.That(
                new[] { failure.Steps[0].Message, failure.Steps[1].Message },
                Is.EqualTo(new[]
                {
                    "别灰心，老板！这次没有达到目标，我们会损失1颗红心。",
                    "营业失败会损失红心，红心归零本局就会结束。",
                }));
        }

        [Test]
        public void TutorialCatalog_ContainsTwentyOneStepsAndNoRemovedSequences()
        {
            string[] activeIds =
            {
                TutorialId.FirstAction,
                TutorialId.FirstBattle,
                TutorialId.FirstBattleSettleHint,
                TutorialId.FirstBattleSettlementOrderHint,
                TutorialId.Settlement,
                TutorialId.SecondAction,
                TutorialId.TimelineNode,
                TutorialId.Flavor,
                TutorialId.Adjustment,
                TutorialId.Boss,
                TutorialId.FirstFailureHeart,
                TutorialId.PassiveItem,
            };

            int stepCount = 0;
            foreach (string id in activeIds)
            {
                TutorialSequenceDefinition sequence = TutorialCatalog.Get(id);
                Assert.That(sequence, Is.Not.Null, id);
                stepCount += sequence.Steps.Count;
            }

            Assert.That(stepCount, Is.EqualTo(21));
            Assert.That(TutorialCatalog.Get("tutorial.prelude.direction_selection"), Is.Null);
            Assert.That(TutorialCatalog.Get("tutorial.core.reward_summary"), Is.Null);
            Assert.That(TutorialCatalog.Get("tutorial.hook.result_heart"), Is.Null);
            Assert.That(TutorialCatalog.Get(TutorialId.Failure), Is.Null);
        }

        [Test]
        public void RemovedPendingTutorials_AreRetiredWithoutPlayback()
        {
            Assert.That(
                TutorialRuntime.ShouldRetirePendingTutorial("tutorial.core.reward_summary"),
                Is.True);
            Assert.That(TutorialRuntime.ShouldRetirePendingTutorial(TutorialId.Failure), Is.True);
            Assert.That(TutorialRuntime.ShouldRetirePendingTutorial(TutorialId.FirstAction), Is.False);
            Assert.That(TutorialRuntime.ShouldRetirePendingTutorial(TutorialId.PassiveItem), Is.False);
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
