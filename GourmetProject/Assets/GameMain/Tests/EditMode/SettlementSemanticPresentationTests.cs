using System.Reflection;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementSemanticPresentationTests
    {
        [TestCase(ScoreLineKind.DishFlat, 20d, "[score]+20[/score]")]
        [TestCase(ScoreLineKind.DishPermanentFlat, 20d, "永久[score]+20[/score]")]
        [TestCase(ScoreLineKind.DishMultiplier, 1.8d, "[multmul]×1.8[/multmul]")]
        [TestCase(ScoreLineKind.DishMultiplierAdd, 0.5d, "[multadd]+0.5[/multadd]")]
        [TestCase(ScoreLineKind.FinalFlat, 20d, "总分 [score]+20[/score]  →  120")]
        [TestCase(ScoreLineKind.FinalMultiplier, 1.8d, "总分 [multmul]×1.8[/multmul]  →  120")]
        [TestCase(ScoreLineKind.Gold, 15d, "[gold]+15[/gold]")]
        [TestCase(ScoreLineKind.SilverItemRoll, 1d, "判定消耗品")]
        [TestCase(ScoreLineKind.ExtraSettlement, 20d, "[benefit]额外结算[/benefit]")]
        [TestCase(ScoreLineKind.TriggerSweetTransfer, 1d, "发动[term]甜蜜传递[/term]")]
        [TestCase(ScoreLineKind.Layer, 3d, "+3")]
        [TestCase(ScoreLineKind.CountAs, 2d, "+2")]
        [TestCase(
            ScoreLineKind.SweetTransferBuffTriggered,
            1.8d,
            "本行[strong]倍率[/strong] [multmul]×1.8[/multmul]")]
        public void ResultText_UsesOnlyTheApprovedSemanticSegments(
            ScoreLineKind kind,
            double value,
            string expected)
        {
            ScoreLine line = BuildLine(kind, value);

            Assert.That(
                SettlementStageView.ResultText(line, BigDouble.Zero, new BigDouble(120)),
                Is.EqualTo(expected));
        }

        [Test]
        public void SilverItemRoll_IsHiddenUntilTheActualRewardAndKeepsFallbackCopyPlain()
        {
            MethodInfo method = typeof(SettlementStageView).GetMethod(
                "ResultHeader",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            Assert.That(
                method.Invoke(null, new object[] { BuildLine(ScoreLineKind.SilverItemRoll, 1d) }),
                Is.EqualTo("银材质"));
            Assert.That(
                SettlementStageView.ShouldShowResultLabel(
                    BuildLine(ScoreLineKind.SilverItemRoll, 1d)),
                Is.False);
        }

        [Test]
        public void SilverAggregateWithoutScoreLines_DoesNotCreateSyntheticPresentation()
        {
            var result = new ScoreResult(
                System.Array.Empty<DishScore>(),
                BigDouble.Zero,
                BigDouble.Zero,
                BigDouble.One,
                silverItemRolls: new[]
                {
                    new SilverItemRollRequest(0.2f, dishInstanceId: 7, materialId: "m_silver"),
                });

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(result);

            Assert.That(plan.Groups, Is.Empty);
        }

        [TestCase(
            ServeCueEffectKind.MultiplierFlat,
            0.5f,
            "倍率 +0.5",
            "[strong]倍率[/strong] [multadd]+0.5[/multadd]")]
        [TestCase(
            ServeCueEffectKind.MultiplierFlat,
            -0.5f,
            "倍率 -0.5（效果转移）",
            "[strong]倍率[/strong] [multadd]-0.5[/multadd]（效果转移）")]
        [TestCase(
            ServeCueEffectKind.MultiplierFactor,
            1.8f,
            "倍率 ×1.8",
            "[strong]倍率[/strong] [multmul]×1.8[/multmul]")]
        [TestCase(
            ServeCueEffectKind.BaseScoreFactor,
            0.7f,
            "基础分 ×0.7",
            "基础分 [multmul]×0.7[/multmul]")]
        [TestCase(
            ServeCueEffectKind.GoldDelta,
            -25f,
            "金币 -25",
            "[gold]金币 -25[/gold]")]
        public void ServeTriggerText_UsesStructuredCueKindWithoutChangingGameplayText(
            ServeCueEffectKind effectKind,
            float value,
            string text,
            string expected)
        {
            var cue = new ServeTriggerCue(
                ServeCueSourceKind.PassiveItem,
                "source",
                "来源",
                1,
                effectKind,
                value,
                text,
                ServeCuePresentationKind.Gain);

            Assert.That(SettlementStageView.SemanticServeTriggerText(cue), Is.EqualTo(expected));
            Assert.That(cue.Text, Is.EqualTo(text));
        }

        [Test]
        public void SettlementLabelBind_KeepsHeaderPlainAndFormatsBodyAtTheTmpBoundary()
        {
            var root = new GameObject("SettlementSemanticLabelTest");
            var headerObject = new GameObject("Header", typeof(TextMeshPro));
            var bodyObject = new GameObject("Body", typeof(TextMeshPro));
            headerObject.transform.SetParent(root.transform);
            bodyObject.transform.SetParent(root.transform);
            SettlementStageLabelView label = root.AddComponent<SettlementStageLabelView>();
            TextMeshPro header = headerObject.GetComponent<TextMeshPro>();
            TextMeshPro body = bodyObject.GetComponent<TextMeshPro>();

            try
            {
                SetField(label, "_headerText", header);
                SetField(label, "_bodyText", body);
                header.richText = false;
                body.richText = false;

                label.Bind(
                    "[term]甜蜜传递[/term]",
                    "[strong]倍率[/strong] [multmul]×1.8[/multmul]",
                    Color.white);

                Assert.That(body.richText, Is.True);
                Assert.That(header.text, Is.EqualTo("[term]甜蜜传递[/term]"));
                Assert.That(
                    body.text,
                    Is.EqualTo(
                        "<b>倍率</b> <material=\"DescriptionMultiplyOutline\"><b><color=#E15A64>" +
                        "×1.8</color></b></material>"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FloatingText_DoesNotInterpretSemanticMarkupInSourceName()
        {
            var root = new GameObject("FloatingTextSemanticSourceTest");
            var sourceObject = new GameObject("Source", typeof(TextMeshPro));
            sourceObject.transform.SetParent(root.transform);
            FloatingTextView view = root.AddComponent<FloatingTextView>();
            TextMeshPro source = sourceObject.GetComponent<TextMeshPro>();

            try
            {
                FieldInfo field = typeof(FloatingTextView).GetField(
                    "_sourceText",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo method = typeof(FloatingTextView).GetMethod(
                    "ConfigureSourceText",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                Assert.That(method, Is.Not.Null);
                field.SetValue(view, source);

                method.Invoke(view, new object[] { "[term]暖心壁灯[/term]" });

                Assert.That(source.text, Is.EqualTo("[term]暖心壁灯[/term]"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ScoreLine BuildLine(ScoreLineKind kind, double value)
        {
            return new ScoreLine(
                ScorePhase.AfterDish,
                kind,
                ScoreSource.FinalModifier("test", "测试来源"),
                1,
                "dish",
                null,
                new BigDouble(value),
                BigDouble.Zero,
                new BigDouble(value),
                string.Empty);
        }

        private static void SetField(
            SettlementStageLabelView target,
            string fieldName,
            TextMeshPro value)
        {
            FieldInfo field = typeof(SettlementStageLabelView).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
