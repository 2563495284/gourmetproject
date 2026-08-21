using System.Linq;
using GourmetProject.Game.Tutorial;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Battle.View;
using GourmetProject.Game.UI.Tooltips;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TutorialFlowTests
    {
        private const string BattleFormPath =
            "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        private const string FoodTipsPath =
            "Assets/GameMain/Content/Prefabs/UI/Tooltips/FoodTipsView.prefab";

        [Test]
        public void FirstBattle_PlacementAndInspectionStepsUseDedicatedAnchors()
        {
            TutorialSequenceDefinition sequence = TutorialCatalog.Get(TutorialId.FirstBattle);

            TutorialStepDefinition table = Step(sequence, "你可以把它拖到餐桌上");
            CollectionAssert.AreEqual(new[] { TutorialAnchorId.Table }, table.Anchors);

            TutorialStepDefinition discard = Step(sequence, "也可以拖进垃圾桶丢弃");
            CollectionAssert.AreEqual(new[] { TutorialAnchorId.Discard }, discard.Anchors);

            int recipeIndex = sequence.Steps
                .Select((step, index) => (step, index))
                .Single(entry => entry.step.Message == "这里是你的初始食谱，里面是经营会抽到的食物。")
                .index;
            TutorialStepDefinition viewTable = sequence.Steps[recipeIndex + 1];
            Assert.That(viewTable.Message, Is.EqualTo("后续获得新的食物也可以从这里查看。"));
            Assert.That(viewTable.Mode, Is.EqualTo(TutorialAdvanceMode.Continue));
            Assert.That(viewTable.AllowTargetInteraction, Is.False);
            CollectionAssert.AreEqual(new[] { TutorialAnchorId.ViewTable }, viewTable.Anchors);
        }

        [Test]
        public void FirstBattle_FoodEffectStepShowsAndHighlightsSummaryTips()
        {
            TutorialSequenceDefinition sequence = TutorialCatalog.Get(TutorialId.FirstBattle);
            TutorialStepDefinition step = Step(
                sequence,
                "每个食物都有自己的特殊效果。老板好好搭配，它们就能发挥更大的作用！");

            Assert.That(step.EnterCommand, Is.EqualTo(TutorialCommand.ShowPreparedFoodTips));
            Assert.That(step.ExitCommand, Is.EqualTo(TutorialCommand.HidePreparedFoodTips));
            CollectionAssert.AreEqual(new[] { TutorialAnchorId.FoodTips }, step.Anchors);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoodTipsPath);
            FoodTipsView tips = prefab != null ? prefab.GetComponent<FoodTipsView>() : null;
            Assert.That(tips, Is.Not.Null);
            Assert.That(tips.SummaryView, Is.Not.Null);
            Assert.That(tips.SummaryView.name, Is.EqualTo("3_Summary"));
        }

        [Test]
        public void FirstBattle_SettlementButtonAndOrderHintsAreSeparateSequences()
        {
            TutorialSequenceDefinition settle = TutorialCatalog.Get(TutorialId.FirstBattleSettleHint);
            Assert.That(settle.Steps.Count, Is.EqualTo(1));
            Assert.That(
                settle.Steps[0].Message,
                Is.EqualTo("等你准备好了，点击「结算」就可以结束本次经营！"));
            CollectionAssert.AreEqual(new[] { TutorialAnchorId.Settle }, settle.Steps[0].Anchors);

            TutorialSequenceDefinition order = TutorialCatalog.Get(TutorialId.FirstBattleSettlementOrderHint);
            Assert.That(order.Steps.Count, Is.EqualTo(1));
            Assert.That(order.Steps[0].Message, Is.EqualTo("食物会从上到下，从左到右开始结算。"));
            CollectionAssert.AreEqual(new[] { TutorialAnchorId.Table }, order.Steps[0].Anchors);
        }

        [Test]
        public void FirstBattleSettlementOrderHint_PlayGuardOnlyAllowsPendingFirstTutorialBattle()
        {
            Assert.That(
                BattleForm.ShouldPlayFirstBattleSettlementOrderHint(
                    isFirstTutorialBattle: true,
                    hasSession: true,
                    isSettled: false,
                    orderHintCompleted: false,
                    tutorialPlaying: false),
                Is.True);
            Assert.That(
                BattleForm.ShouldPlayFirstBattleSettlementOrderHint(
                    isFirstTutorialBattle: false,
                    hasSession: true,
                    isSettled: false,
                    orderHintCompleted: false,
                    tutorialPlaying: false),
                Is.False);
            Assert.That(
                BattleForm.ShouldPlayFirstBattleSettlementOrderHint(
                    isFirstTutorialBattle: true,
                    hasSession: true,
                    isSettled: true,
                    orderHintCompleted: false,
                    tutorialPlaying: false),
                Is.False);
            Assert.That(
                BattleForm.ShouldPlayFirstBattleSettlementOrderHint(
                    isFirstTutorialBattle: true,
                    hasSession: true,
                    isSettled: false,
                    orderHintCompleted: true,
                    tutorialPlaying: false),
                Is.False);
            Assert.That(
                BattleForm.ShouldPlayFirstBattleSettlementOrderHint(
                    isFirstTutorialBattle: true,
                    hasSession: true,
                    isSettled: false,
                    orderHintCompleted: false,
                    tutorialPlaying: true),
                Is.False);
        }

        [Test]
        public void BattleForm_ViewTableTutorialAnchorTargetsViewTableButton()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleFormPath);
            BattleInfoColumn info = prefab != null
                ? prefab.GetComponentInChildren<BattleInfoColumn>(includeInactive: true)
                : null;

            Assert.That(info, Is.Not.Null);
            Assert.That(info.ViewTableButtonRect, Is.Not.Null);
            Assert.That(info.ViewTableButtonRect.name, Is.EqualTo("ViewTableButton"));
        }

        private static TutorialStepDefinition Step(
            TutorialSequenceDefinition sequence,
            string message)
        {
            Assert.That(sequence, Is.Not.Null);
            return sequence.Steps.Single(step => step.Message == message);
        }
    }
}
