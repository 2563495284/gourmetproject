using System;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Core.Save;
using GourmetProject.Game.Save;
using GourmetProject.Game.Tutorial;
using GourmetProject.Runtime;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TutorialCatalogFlowTests
    {
        [Test]
        public void DirectionSelection_UsesApprovedCopyAndWaitsForNewGame()
        {
            TutorialSequenceDefinition sequence = Require(TutorialId.DirectionSelection, 2);

            AssertStep(
                sequence,
                0,
                "老板，我们来选择餐厅的经营方向吧！这次我们经营「甜品」吧。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.DirectionName,
                TutorialAnchorId.DirectionDescription);
            AssertStep(
                sequence,
                1,
                "点击「新游戏」吧。铛铛会陪你一起把店开起来！",
                TutorialAdvanceMode.Signal,
                TutorialSignal.DirectionConfirmed,
                TutorialAnchorId.DirectionStart);
            Assert.That(sequence.Steps[1].AllowTargetInteraction, Is.True);
        }

        [Test]
        public void FirstAction_UsesApprovedThreeStepFlow()
        {
            TutorialSequenceDefinition sequence = Require(TutorialId.FirstAction, 3);

            AssertStep(
                sequence,
                0,
                "这是行动卡，每次行动都会消耗一定的时间，完成会带来收益。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ActionCard0);
            AssertStep(
                sequence,
                1,
                "卡片上的图标，表示这次行动的额外奖励。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ActionReward0);
            AssertStep(
                sequence,
                2,
                "点击这张日常营业卡，开始我们的营业吧！",
                TutorialAdvanceMode.Signal,
                TutorialSignal.ActionPicked,
                TutorialAnchorId.ActionCard0);
        }

        [Test]
        public void FirstBattle_HasSixInformationalStepsAndExpectedPanelCommands()
        {
            TutorialSequenceDefinition sequence = Require(TutorialId.FirstBattle, 6);

            AssertStep(
                sequence,
                0,
                "偷偷告诉老板营业的秘诀，就是把尽可能多的食物摆上餐桌，获得足够的美味值。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.Table);
            AssertStep(
                sequence,
                1,
                "老板，看这里！食物会从食谱中抽取，展示在出菜口。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ServingOutlet);
            AssertStep(
                sequence,
                2,
                "你可以把它拖到餐桌上，也可以拖进垃圾桶丢弃。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ServingOutlet,
                TutorialAnchorId.Table,
                TutorialAnchorId.Discard);
            AssertStep(
                sequence,
                3,
                "这里是你的初始食谱，里面是经营会抽到的食物。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.RecipePanel);
            AssertStep(
                sequence,
                4,
                "每个食物都有自己的特殊效果。老板好好搭配，它们就能发挥更大的作用！",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.FoodTips);
            AssertStep(
                sequence,
                5,
                "这里是需要达到的美味值。努力超过它吧！",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ScoreSection,
                TutorialAnchorId.ScoreTitle,
                TutorialAnchorId.ScoreMeter);

            Assert.That(sequence.Steps[3].EnterCommand, Is.EqualTo(TutorialCommand.OpenInitialRecipe));
            Assert.That(sequence.Steps[3].ExitCommand, Is.EqualTo(TutorialCommand.CloseInitialRecipe));
            Assert.That(sequence.Steps[4].EnterCommand, Is.EqualTo(TutorialCommand.ShowPreparedFoodTips));
            Assert.That(sequence.Steps[4].ExitCommand, Is.EqualTo(TutorialCommand.HidePreparedFoodTips));

            foreach (TutorialStepDefinition step in sequence.Steps)
            {
                Assert.That(step.Mode, Is.EqualTo(TutorialAdvanceMode.Continue));
                Assert.That(step.Signal, Is.Empty);
                Assert.That(step.AllowTargetInteraction, Is.False);
            }
        }

        [Test]
        public void FirstBattleSettleHint_IsOptionalContinueStepOnSettleButton()
        {
            TutorialSequenceDefinition sequence = Require(TutorialId.FirstBattleSettleHint, 1);

            AssertStep(
                sequence,
                0,
                "等你准备好了，点击「结算」就可以结束本次经营！",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.Settle);
            Assert.That(sequence.Steps[0].AllowTargetInteraction, Is.False);
        }

        [Test]
        public void RewardSecondActionAndTimelineNode_UseApprovedInteractionFlow()
        {
            TutorialSequenceDefinition reward = Require(TutorialId.RewardSummary, 1);
            AssertStep(
                reward,
                0,
                "这里是本次营业奖励，老板快领取吧。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.RewardList);

            TutorialSequenceDefinition secondAction = Require(TutorialId.SecondAction, 1);
            AssertStep(
                secondAction,
                0,
                "这是火热营业，目标更高，但奖励规格也更高。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ActionCard2);
            Assert.That(secondAction.Steps[0].AllowTargetInteraction, Is.False);

            TutorialSequenceDefinition timeline = Require(TutorialId.TimelineNode, 2);
            AssertStep(
                timeline,
                0,
                "普通行动推进时间轴时，经过的节点会出现在行动卡中。",
                TutorialAdvanceMode.Continue,
                string.Empty,
                TutorialAnchorId.ActionAxis);
            AssertStep(
                timeline,
                1,
                "这些是节点行动，不会消耗天数。这个节点会结算利息。",
                TutorialAdvanceMode.Signal,
                TutorialSignal.TimelineNodePicked,
                TutorialAnchorId.ActionCard0);
            Assert.That(timeline.Steps[1].AllowTargetInteraction, Is.True);
        }

        [Test]
        public void UnlockHooks_UseApprovedCopyAndStepCounts()
        {
            TutorialSequenceDefinition flavor = Require(TutorialId.Flavor, 2);
            AssertMessages(
                flavor,
                "老板，食物现在有风味啦！每个食物只有 1 个风味位哦。",
                "风味会改变食物的属性和结算效果，强化箱道具可以帮我们为食物附加风味。");
            AssertAllAnchors(flavor, TutorialAnchorId.AcquiredActiveItem);

            TutorialSequenceDefinition material = Require(TutorialId.Material, 2);
            AssertMessages(
                material,
                "老板，餐桌现在有材质啦！每个餐桌格只有 1 个材质哦。",
                "放置在餐桌格上的食物会获得对应效果；强化箱道具可以帮我们为餐桌附加材质。");
            AssertAllAnchors(material, TutorialAnchorId.AcquiredActiveItem);

            TutorialSequenceDefinition adjustment = Require(TutorialId.Adjustment, 1);
            AssertMessages(
                adjustment,
                "这是调整单！它可以修改节点和行动。");
            AssertAllAnchors(adjustment, TutorialAnchorId.AcquiredActiveItem);

            TutorialSequenceDefinition passiveItem = Require(TutorialId.PassiveItem, 1);
            AssertMessages(
                passiveItem,
                "装饰品获得后会永久生效。这可是我们提升餐厅实力的重要方面呢！");
            AssertAllAnchors(passiveItem, TutorialAnchorId.AcquiredPassiveItem);

            AssertMessages(
                Require(TutorialId.Boss, 2),
                "我们终于来到星级评鉴啦！星级评鉴拥有特殊规则，需要的美味值也更高。",
                "完成评鉴可以获得星星，以及丰厚的奖励！");
        }

        [Test]
        public void FirstFailureHeart_UsesNewIndependentIdAndTwoApprovedSteps()
        {
            TutorialSequenceDefinition sequence = Require(TutorialId.FirstFailureHeart, 2);

            Assert.That(TutorialId.FirstFailureHeart, Is.Not.EqualTo(TutorialId.ResultHeart));
            Assert.That(TutorialId.FirstFailureHeart, Is.Not.EqualTo(TutorialId.Failure));
            AssertMessages(
                sequence,
                "别灰心，老板！这次没有达到目标，我们会损失❤️。",
                "日常营业、火热营业和星级评鉴失败都会损失1颗。❤️归零，本局就会结束。");
            foreach (TutorialStepDefinition step in sequence.Steps)
            {
                Assert.That(step.Mode, Is.EqualTo(TutorialAdvanceMode.Continue));
                Assert.That(step.Pose, Is.EqualTo(TutorialMascotPose.Remind));
                CollectionAssert.AreEqual(new[] { TutorialAnchorId.Hearts }, step.Anchors);
            }
        }

        [TestCase(false, false, false, true)]
        [TestCase(true, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        public void FirstFailureHeartEligibility_RequiresFirstAvailableLoss(
            bool isWin,
            bool completed,
            bool tutorialPlaying,
            bool expected)
        {
            Assert.That(
                TutorialRuntime.ShouldPlayFirstFailureHeart(isWin, completed, tutorialPlaying),
                Is.EqualTo(expected));
        }

        [Test]
        public void LegacyResultAndFailureCopy_RemainsAvailableButUsesOldIds()
        {
            TutorialSequenceDefinition win = TutorialCatalog.BuildResultHeart(isWin: true);
            TutorialSequenceDefinition loss = TutorialCatalog.BuildResultHeart(isWin: false);
            TutorialSequenceDefinition settlement = Require(TutorialId.Settlement, 1);
            TutorialSequenceDefinition failure = Require(TutorialId.Failure, 1);

            Assert.That(win.Id, Is.EqualTo(TutorialId.ResultHeart));
            Assert.That(loss.Id, Is.EqualTo(TutorialId.ResultHeart));
            Assert.That(
                win.Steps[0].Message,
                Is.EqualTo("太棒了，老板！这次经营成功，❤️红心不会减少。红心代表餐厅还能承受失败的次数：日常营业、火热营业和星级评鉴失败都会损失1颗；红心归零，本局就会结束。"));
            Assert.That(
                loss.Steps[0].Message,
                Is.EqualTo("别灰心，老板！这次没有达到目标，失败会让我们损失❤️红心。日常营业、火热营业和星级评鉴失败都会损失1颗；红心归零，本局就会结束。"));
            Assert.That(
                settlement.Steps[0].Message,
                Is.EqualTo("这里是本次营业的结果。总美味值达到目标即为成功，否则营业失败。"));
            Assert.That(
                failure.Steps[0].Message,
                Is.EqualTo("别灰心，老板！之后铛铛会在经营结果里说明红心规则。"));
        }

        private static TutorialSequenceDefinition Require(string id, int expectedStepCount)
        {
            TutorialSequenceDefinition sequence = TutorialCatalog.Get(id);
            Assert.That(sequence, Is.Not.Null, $"Missing tutorial sequence: {id}");
            Assert.That(sequence.Id, Is.EqualTo(id));
            Assert.That(sequence.Steps.Count, Is.EqualTo(expectedStepCount));
            return sequence;
        }

        private static void AssertStep(
            TutorialSequenceDefinition sequence,
            int index,
            string message,
            TutorialAdvanceMode mode,
            string signal,
            params string[] anchors)
        {
            TutorialStepDefinition step = sequence.Steps[index];
            Assert.That(step.Message, Is.EqualTo(message));
            Assert.That(step.Mode, Is.EqualTo(mode));
            Assert.That(step.Signal, Is.EqualTo(signal));
            CollectionAssert.AreEqual(anchors, step.Anchors);
        }

        private static void AssertMessages(
            TutorialSequenceDefinition sequence,
            params string[] expectedMessages)
        {
            Assert.That(sequence.Steps.Count, Is.EqualTo(expectedMessages.Length));
            for (int i = 0; i < expectedMessages.Length; i++)
            {
                Assert.That(sequence.Steps[i].Message, Is.EqualTo(expectedMessages[i]));
                Assert.That(sequence.Steps[i].Mode, Is.EqualTo(TutorialAdvanceMode.Continue));
                Assert.That(sequence.Steps[i].Signal, Is.Empty);
            }
        }

        private static void AssertAllAnchors(
            TutorialSequenceDefinition sequence,
            params string[] expectedAnchors)
        {
            foreach (TutorialStepDefinition step in sequence.Steps)
            {
                CollectionAssert.AreEqual(expectedAnchors, step.Anchors);
            }
        }
    }

    public sealed class TutorialFirstFailurePersistenceTests
    {
        [Test]
        public void Victory_CompletesResultCallbackWithoutConsumingFirstFailureId()
        {
            var save = new MemorySaveService();
            using (new GameAppSaveScope(save))
            {
                bool callbackInvoked = false;

                bool played = TutorialRuntime.PlayFirstFailureHeart(
                    isWin: true,
                    onComplete: () => callbackInvoked = true);

                Assert.That(played, Is.False);
                Assert.That(callbackInvoked, Is.True);
                Assert.That(TutorialProgressService.IsCompleted(TutorialId.FirstFailureHeart), Is.False);
                CollectionAssert.DoesNotContain(
                    TutorialProgressService.Pending(),
                    TutorialId.FirstFailureHeart);
            }
        }

        [Test]
        public void LegacyResultHeartCompletion_DoesNotSuppressNewFirstFailure()
        {
            var save = new MemorySaveService();
            using (new GameAppSaveScope(save))
            {
                TutorialProgressService.Complete(TutorialId.ResultHeart);

                Assert.That(TutorialProgressService.IsCompleted(TutorialId.ResultHeart), Is.True);
                bool newVersionCompleted = TutorialProgressService.IsCompleted(TutorialId.FirstFailureHeart);
                Assert.That(newVersionCompleted, Is.False);
                Assert.That(
                    TutorialRuntime.ShouldPlayFirstFailureHeart(
                        isWin: false,
                        completed: newVersionCompleted,
                        tutorialPlaying: false),
                    Is.True);
            }
        }

        [Test]
        public void FirstFailureHeartCompletion_PreventsReplay()
        {
            var save = new MemorySaveService();
            using (new GameAppSaveScope(save))
            {
                TutorialProgressService.Complete(TutorialId.FirstFailureHeart);

                bool completed = TutorialProgressService.IsCompleted(TutorialId.FirstFailureHeart);
                Assert.That(completed, Is.True);
                Assert.That(
                    TutorialRuntime.ShouldPlayFirstFailureHeart(
                        isWin: false,
                        completed: completed,
                        tutorialPlaying: false),
                    Is.False);
            }
        }

        private sealed class MemorySaveService : ISaveService
        {
            private readonly Dictionary<string, object> _slots = new Dictionary<string, object>();

            public void Save<T>(string slot, T data)
            {
                _slots[slot] = data;
            }

            public bool TryLoad<T>(string slot, out T data)
            {
                if (_slots.TryGetValue(slot, out object value) && value is T typed)
                {
                    data = typed;
                    return true;
                }

                data = default;
                return false;
            }

            public bool Has(string slot) => _slots.ContainsKey(slot);

            public void Delete(string slot) => _slots.Remove(slot);

            public IEnumerable<string> ListSlots() => _slots.Keys;
        }

        private sealed class GameAppSaveScope : IDisposable
        {
            private readonly FieldInfo _saveField;
            private readonly object _previousSave;

            public GameAppSaveScope(ISaveService save)
            {
                _saveField = typeof(GameApp).GetField(
                    "<Save>k__BackingField",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (_saveField == null)
                {
                    throw new InvalidOperationException("GameApp.Save backing field not found.");
                }

                _previousSave = _saveField.GetValue(null);
                _saveField.SetValue(null, save);
            }

            public void Dispose()
            {
                _saveField.SetValue(null, _previousSave);
            }
        }
    }
}
