using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSequencerTests
    {
        [Test]
        public void ZeroValueDishSkillStillBuildsSettlementCue()
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.DishFlat,
                new ScoreSource(ScoreSourceType.DishSkill, "pudding_empty_table", "布丁", 1, "pudding"),
                1,
                "pudding",
                null,
                0f,
                0f,
                0f,
                "布丁: 美味度 -0");

            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { line, null };
            bool built = (bool)method.Invoke(null, args);

            Assert.That(built, Is.True);
            Assert.That(args[1], Is.Not.Null);
        }

        [Test]
        public void ZeroValueWithoutExecutedSkillRemainsFiltered()
        {
            var line = new ScoreLine(
                ScorePhase.Final,
                ScoreLineKind.FinalFlat,
                ScoreSource.FinalModifier("none", "无变化"),
                0,
                string.Empty,
                null,
                0f,
                0f,
                0f,
                "无变化");

            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { line, null };
            bool built = (bool)method.Invoke(null, args);

            Assert.That(built, Is.False);
            Assert.That(args[1], Is.Null);
        }

        [Test]
        public void SweetTransferBatchesScopeTargetsButKeepsRecipientsSeparate()
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "BuildDishSkillBatchKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            ScoreLine recipientTwoSelf = SweetTransferLine(2, 2);
            ScoreLine recipientTwoNeighbor = SweetTransferLine(2, 3);
            ScoreLine recipientSixSelf = SweetTransferLine(6, 6);

            string recipientTwoKey = (string)method.Invoke(null, new object[] { recipientTwoSelf });
            string recipientTwoNeighborKey = (string)method.Invoke(null, new object[] { recipientTwoNeighbor });
            string recipientSixKey = (string)method.Invoke(null, new object[] { recipientSixSelf });

            Assert.That(recipientTwoKey, Is.Not.Null.And.Not.Empty);
            Assert.That(recipientTwoNeighborKey, Is.EqualTo(recipientTwoKey));
            Assert.That(recipientSixKey, Is.Not.EqualTo(recipientTwoKey));
        }

        [Test]
        public void DishSkillFeedbackDistinguishesActiveAndPassiveFlatBonus()
        {
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishFlat, 1, 1)), Is.EqualTo(SettlementDishFeedbackKind.ActiveFlatBonus));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishFlat, 1, 2)), Is.EqualTo(SettlementDishFeedbackKind.PassiveFlatBonus));
        }

        [Test]
        public void DishSkillFeedbackDistinguishesMultiplierModes()
        {
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplier, 1, 1)), Is.EqualTo(SettlementDishFeedbackKind.ActiveMultiplier));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplier, 1, 2)), Is.EqualTo(SettlementDishFeedbackKind.PassiveMultiplier));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplierAdd, 1, 1)), Is.EqualTo(SettlementDishFeedbackKind.ActiveMultiplierAdd));
            Assert.That(FeedbackKind(DishSkillLine(ScoreLineKind.DishMultiplierAdd, 1, 2)), Is.EqualTo(SettlementDishFeedbackKind.PassiveMultiplierAdd));
        }

        [Test]
        public void SweetTransferFeedbackPreservesActiveAndPassiveModes()
        {
            Assert.That(FeedbackKind(SweetTransferLine(2, 2)), Is.EqualTo(SettlementDishFeedbackKind.ActiveFlatBonus));
            Assert.That(FeedbackKind(SweetTransferLine(2, 3)), Is.EqualTo(SettlementDishFeedbackKind.PassiveFlatBonus));
        }

        [Test]
        public void SweetTransferBaseBatchKeepsOriginalOwnerAsVisualSource()
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "ResolveSweetTransferSourceDishId",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(SettlementScopeSignal), typeof(string) },
                null);
            Assert.That(method, Is.Not.Null);

            var scope = new SettlementScopeSignal(7, 2, null);
            int sourceDishId = (int)method.Invoke(null, new object[] { scope, "base:skill|A<甜蜜传递>" });

            Assert.That(sourceDishId, Is.EqualTo(7));
        }

        [Test]
        public void SettlementPlanStartsWithOneBatchForAllDishScores()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishInstance first = DishInstanceForTest(1, "first", shape, 0);
            DishInstance second = DishInstanceForTest(2, "second", shape, 1);
            GameObject firstObject = new("FirstDishView");
            GameObject secondObject = new("SecondDishView");

            try
            {
                DishPieceView firstView = firstObject.AddComponent<DishPieceView>();
                DishPieceView secondView = secondObject.AddComponent<DishPieceView>();
                SetViewInstance(firstView, first);
                SetViewInstance(secondView, second);

                var dishViews = new Dictionary<int, DishPieceView>
                {
                    [first.Id] = firstView,
                    [second.Id] = secondView,
                };
                var result = new ScoreResult(
                    new[]
                    {
                        new DishScore(second.Id, second.Def.Id, 7f, 0f, 1f),
                        new DishScore(first.Id, first.Def.Id, 4f, 0f, 1f),
                    },
                    11f,
                    0f,
                    1f,
                    Array.Empty<ScoreLine>());
                var baseline = new SettlementBaselineSnapshot();
                baseline.Capture(first);
                baseline.Capture(second);

                MethodInfo buildPlan = typeof(SettlementSequencer).GetMethod(
                    "BuildSettlementPlaybackPlan",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(buildPlan, Is.Not.Null);
                object plan = buildPlan.Invoke(null, new object[] { result, dishViews, baseline });
                IList steps = (IList)plan.GetType().GetProperty("Steps")?.GetValue(plan);

                Assert.That(steps, Is.Not.Null);
                Assert.That(steps.Count, Is.EqualTo(2));
                Assert.That(StepDishInstanceId(steps[0]), Is.EqualTo(second.Id));
                Assert.That(StepDishInstanceId(steps[1]), Is.EqualTo(first.Id));
                Assert.That(StepBatchKey(steps[0]), Is.Not.Empty.And.EqualTo(StepBatchKey(steps[1])));
                Assert.That(StepFeedbackKind(steps[0]), Is.EqualTo(SettlementDishFeedbackKind.DishBase));
                Assert.That(StepFeedbackKind(steps[1]), Is.EqualTo(SettlementDishFeedbackKind.DishBase));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void CopySkillFeedbackUsesDedicatedKind()
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.CopySkill,
                new ScoreSource(ScoreSourceType.DishSkill, "copy", "复制", 1, "copy_dish"),
                1,
                "copy_dish",
                null,
                1f,
                0f,
                1f,
                "复制技能 +1");

            Assert.That(FeedbackKind(line), Is.EqualTo(SettlementDishFeedbackKind.CopySkillTriggered));
        }

        [Test]
        public void SweetTransferExtraSettlementCueRevealsTransferredCard()
        {
            var line = new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.ExtraSettlement,
                new ScoreSource(
                    ScoreSourceType.DishSkill,
                    "sk_nougat",
                    "牛轧糖<甜蜜传递>",
                    5,
                    "daifuku"),
                5,
                "daifuku",
                null,
                1f,
                0f,
                1f,
                "牛轧糖<甜蜜传递>: 技能额外触发 +1 次");

            object cue = BuildCue(line);
            PropertyInfo revealProperty = cue.GetType().GetProperty("Reveal");
            Assert.That(revealProperty, Is.Not.Null);

            var reveal = (SettlementRevealSignal)revealProperty.GetValue(cue);
            Assert.That(reveal.DishInstanceId, Is.EqualTo(5));
            Assert.That(reveal.TransferredDelta, Is.EqualTo(1));
            Assert.That(reveal.IsEmpty, Is.False);
        }

        private static ScoreLine SweetTransferLine(int recipientInstanceId, int affectedInstanceId)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.DishFlat,
                new ScoreSource(
                    ScoreSourceType.DishSkill,
                    "sk_toffee",
                    "太妃糖<甜蜜传递>",
                    recipientInstanceId,
                    "lollipop"),
                affectedInstanceId,
                "target",
                null,
                3f,
                0f,
                3f,
                "太妃糖<甜蜜传递>: 美味度 +3");
        }

        private static ScoreLine DishSkillLine(ScoreLineKind kind, int sourceInstanceId, int affectedInstanceId)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                new ScoreSource(
                    ScoreSourceType.DishSkill,
                    "sk_bonus",
                    "周围加成",
                    sourceInstanceId,
                    "source"),
                affectedInstanceId,
                "target",
                null,
                kind == ScoreLineKind.DishMultiplier ? 2f : 3f,
                0f,
                kind == ScoreLineKind.DishMultiplier ? 2f : 3f,
                "周围加成");
        }

        private static SettlementDishFeedbackKind FeedbackKind(ScoreLine line)
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "BuildDishFeedbackKind",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (SettlementDishFeedbackKind)method.Invoke(null, new object[] { line });
        }

        private static object BuildCue(ScoreLine line)
        {
            MethodInfo method = typeof(SettlementSequencer).GetMethod(
                "TryBuildCue",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            object[] args = { line, null };
            bool built = (bool)method.Invoke(null, args);
            Assert.That(built, Is.True);
            Assert.That(args[1], Is.Not.Null);
            return args[1];
        }

        private static DishInstance DishInstanceForTest(int id, string dishId, DishShape shape, int x)
        {
            var def = new DishDef(
                dishId,
                dishId,
                id + 3,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                false);
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static void SetViewInstance(DishPieceView view, DishInstance instance)
        {
            MethodInfo setter = typeof(DishPieceView)
                .GetProperty("Instance", BindingFlags.Instance | BindingFlags.Public)
                ?.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null);
            setter.Invoke(view, new object[] { instance });
        }

        private static int StepDishInstanceId(object step)
        {
            return (int)step.GetType().GetProperty("DishInstanceId")?.GetValue(step);
        }

        private static string StepBatchKey(object step)
        {
            object cue = step.GetType().GetProperty("Cue")?.GetValue(step);
            return (string)cue?.GetType().GetProperty("BatchKey")?.GetValue(cue);
        }

        private static SettlementDishFeedbackKind StepFeedbackKind(object step)
        {
            object cue = step.GetType().GetProperty("Cue")?.GetValue(step);
            return (SettlementDishFeedbackKind)cue?.GetType().GetProperty("FeedbackKind")?.GetValue(cue);
        }
    }
}
