using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SettlementPresentationPlanTests
    {
        [Test]
        public void Build_PreservesBaseOrderAndEveryResultLine()
        {
            ScoreSource firstDish = new(ScoreSourceType.Dish, "dish_a", "菜 A", 1, "dish_a");
            ScoreSource secondDish = new(ScoreSourceType.Dish, "dish_b", "菜 B", 2, "dish_b");
            ScoreSource skill = new(ScoreSourceType.DishSkill, "skill_a", "菜 A", 1, "dish_a");
            var lines = new List<ScoreLine>
            {
                Line(ScoreLineKind.DishBase, firstDish, 1, 10f, 0f, 10f),
                Line(ScoreLineKind.DishBase, secondDish, 2, 4f, 0f, 4f),
                Line(ScoreLineKind.DishFlat, skill, 1, 3f, 0f, 3f, executionGroupId: 7),
                Line(ScoreLineKind.DishMultiplier, skill, 1, 2f, 1f, 2f, executionGroupId: 7),
                Line(ScoreLineKind.DishFlat, skill, 2, 2f, 0f, 2f, executionGroupId: 7),
                Line((ScoreLineKind)999, skill, 2, 1f, 0f, 1f, executionGroupId: 8),
            };
            var result = new ScoreResult(
                new[]
                {
                    new DishScore(1, "dish_a", 10f, 3f, 2f),
                    new DishScore(2, "dish_b", 4f, 2f, 1f),
                },
                rawSum: 32f,
                finalFlat: 0f,
                finalMultiplier: 1f,
                scoreLines: lines);

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(result);

            Assert.That(plan.BaseBeats.Count, Is.EqualTo(2));
            Assert.That(plan.BaseBeats[0].DishInstanceId, Is.EqualTo(1));
            Assert.That(plan.BaseBeats[1].DishInstanceId, Is.EqualTo(2));
            Assert.That(plan.Groups.Count, Is.EqualTo(2));
            Assert.That(plan.Groups[0].GroupId, Is.EqualTo(7));
            Assert.That(plan.Groups[0].Lines, Has.Count.EqualTo(3));
            Assert.That(plan.Groups[1].Lines, Has.Count.EqualTo(1));
            Assert.That((int)plan.Groups[1].Lines[0].Kind, Is.EqualTo(999));
            Assert.That(plan.ResultBeatCount, Is.EqualTo(7));
        }

        [Test]
        public void RunningLedger_RecomputesDishAndGlobalTotalsAfterEveryLine()
        {
            var ledger = new SettlementRunningLedger(
                new[]
                {
                    new DishScore(1, "dish_a", 10f, 5f, 2f),
                    new DishScore(2, "dish_b", 4f, 0f, 1f),
                },
                baselineSnapshot: null);

            Assert.That(ledger.ApplyBase(1, 10f), Is.EqualTo(10f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(10));
            Assert.That(ledger.ApplyBase(2, 4f), Is.EqualTo(4f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(14));

            ledger.Apply(Line(ScoreLineKind.DishFlat, null, 1, 5f, 0f, 5f));
            Assert.That(ledger.ContributionFor(1), Is.EqualTo(15f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(19));

            ledger.Apply(Line(ScoreLineKind.DishMultiplier, null, 1, 2f, 1f, 2f));
            Assert.That(ledger.ContributionFor(1), Is.EqualTo(30f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(34));

            ledger.Apply(Line(ScoreLineKind.FinalFlat, null, 0, 3f, 0f, 3f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(37));
            ledger.Apply(Line(ScoreLineKind.FinalMultiplier, null, 0, 2f, 1f, 2f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(74));
        }

        [Test]
        public void RunningLedger_UsesOfficialCeilingRule()
        {
            var ledger = new SettlementRunningLedger(
                new[] { new DishScore(1, "dish_a", 1f, 0f, 1.1f) },
                baselineSnapshot: null);

            ledger.ApplyBase(1, 1f);
            ledger.Apply(Line(ScoreLineKind.DishMultiplier, null, 1, 1.1f, 1f, 1.1f));

            Assert.That(ledger.ContributionFor(1), Is.EqualTo(2f));
            Assert.That(ledger.CurrentTotal, Is.EqualTo(2));
        }

        [Test]
        public void Calculator_PropagatesOneExecutionGroupToLinesAndEvents()
        {
            var rule = new SkillRuleDef(
                "rule_add_flat",
                "skill_add_flat",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                1,
                new[] { 3f },
                Array.Empty<string>());
            var skill = new SkillDef(
                "skill_add_flat",
                "加分技能",
                string.Empty,
                Array.Empty<string>(),
                new[] { rule });
            var definition = new DishDef(
                "dish_a",
                "菜 A",
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                new[] { skill.Id },
                string.Empty,
                false);
            var db = new GameplayDatabase(
                new[] { definition },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var table = new DiningTable(1, 1);
            var dish = new DishInstance(
                1,
                definition,
                new Placement(definition.Shape, 0, new GridPos(0, 0)),
                definition.SkillIds,
                Array.Empty<string>());
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(table, db);
            ScoreLine scoreLine = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.DishFlat);

            Assert.That(scoreLine.ExecutionGroupId, Is.GreaterThan(0));
            ScoreEvent[] groupEvents = result.ScoreEvents
                .Where(scoreEvent => scoreEvent.ExecutionGroupId == scoreLine.ExecutionGroupId)
                .ToArray();
            Assert.That(groupEvents.Any(item => item.Type == ScoreEventType.EffectStarted), Is.True);
            Assert.That(groupEvents.Any(item => item.Type == ScoreEventType.EffectFinished), Is.True);
            Assert.That(
                groupEvents.Where(item => item.Type == ScoreEventType.CommandExecuted).All(
                    item => item.ExecutionGroupId == scoreLine.ExecutionGroupId),
                Is.True);
        }

        [Test]
        public void StageText_CreatesReadableTextWithFontMaterial()
        {
            GameObject root = new("SettlementStageTextTest");
            try
            {
                TextMesh text = SettlementStageView.CreateText(
                    root.transform,
                    "Result",
                    "分数 +3 → 18",
                    0f,
                    32,
                    0.09f,
                    3);

                Assert.That(text.text, Is.EqualTo("分数 +3 → 18"));
                Assert.That(text.characterSize, Is.EqualTo(0.09f));
                Assert.That(text.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(text.font, Is.Not.Null);
                MeshRenderer renderer = text.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.sharedMaterial, Is.Not.Null);
                Assert.That(renderer.bounds.size.y, Is.GreaterThan(0.05f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ResultText_DishValueChangesOmitPostChangeContribution()
        {
            Assert.That(
                SettlementStageView.ResultText(
                    Line(ScoreLineKind.DishFlat, null, 1, 3f, 0f, 3f),
                    dishContribution: 18f,
                    runningTotal: 18),
                Is.EqualTo("分数 +3"));
            Assert.That(
                SettlementStageView.ResultText(
                    Line(ScoreLineKind.DishMultiplier, null, 1, 2f, 1f, 2f),
                    dishContribution: 18f,
                    runningTotal: 18),
                Is.EqualTo("倍率 ×2"));
            Assert.That(
                SettlementStageView.ResultText(
                    Line(ScoreLineKind.DishMultiplierAdd, null, 1, 0.5f, 1f, 1.5f),
                    dishContribution: 18f,
                    runningTotal: 18),
                Is.EqualTo("倍率 +0.5"));
        }

        [Test]
        public void AttributePalette_MapsFourModesToStableColors()
        {
            Assert.That(
                (Color32)SettlementAttributePalette.For(ScoreLineKind.DishFlat),
                Is.EqualTo(new Color32(246, 196, 83, 255)));
            Assert.That(
                (Color32)SettlementAttributePalette.For(ScoreLineKind.DishMultiplierAdd),
                Is.EqualTo(new Color32(57, 208, 176, 255)));
            Assert.That(
                (Color32)SettlementAttributePalette.For(ScoreLineKind.DishMultiplier),
                Is.EqualTo(new Color32(255, 90, 95, 255)));
            Assert.That(
                (Color32)SettlementAttributePalette.For(ScoreLineKind.CopySkill),
                Is.EqualTo(new Color32(169, 120, 255, 255)));
        }

        [TestCase(0, 0, (int)SettlementSweetTransferTransition.None)]
        [TestCase(0, 3, (int)SettlementSweetTransferTransition.Begin)]
        [TestCase(3, 3, (int)SettlementSweetTransferTransition.Keep)]
        [TestCase(3, 4, (int)SettlementSweetTransferTransition.Switch)]
        [TestCase(3, 0, (int)SettlementSweetTransferTransition.Clear)]
        public void SweetTransferTransition_SeparatesKeepSwitchAndClear(
            int currentSourceDishId,
            int nextSourceDishId,
            int expected)
        {
            Assert.That(
                SettlementSweetTransferTransitionResolver.Resolve(
                    currentSourceDishId,
                    nextSourceDishId),
                Is.EqualTo((SettlementSweetTransferTransition)expected));
        }

        [Test]
        public void StageFeedback_UsesDedicatedPinkKindForSweetTransferResults()
        {
            Assert.That(
                SettlementStageView.FeedbackFor(
                    Line(ScoreLineKind.TriggerSweetTransfer, null, 1, 1f, 0f, 1f)),
                Is.EqualTo(SettlementDishFeedbackKind.SweetTransferResult));
            Assert.That(
                SettlementStageView.FeedbackFor(
                    Line(ScoreLineKind.TriggeredSweetTransferSource, null, 1, 1f, 0f, 1f)),
                Is.EqualTo(SettlementDishFeedbackKind.SweetTransferResult));
            Assert.That(
                SettlementStageView.FeedbackFor(
                    Line(ScoreLineKind.CopySkill, null, 1, 1f, 0f, 1f)),
                Is.EqualTo(SettlementDishFeedbackKind.CopySkillTriggered));
            Assert.That(
                (Color32)SettlementStageView.ResultThemeFor(
                    Line(ScoreLineKind.TriggerSweetTransfer, null, 1, 1f, 0f, 1f)),
                Is.EqualTo(new Color32(255, 77, 173, 255)));
            Assert.That(
                (Color32)SettlementStageView.ResultThemeFor(
                    Line(ScoreLineKind.CopySkill, null, 1, 1f, 0f, 1f)),
                Is.EqualTo(new Color32(169, 120, 255, 255)));
        }

        private static ScoreLine Line(
            ScoreLineKind kind,
            ScoreSource source,
            int dishInstanceId,
            float value,
            float before,
            float after,
            int executionGroupId = 0)
        {
            return new ScoreLine(
                ScorePhase.DishSkills,
                kind,
                source,
                dishInstanceId,
                string.Empty,
                null,
                value,
                before,
                after,
                kind.ToString(),
                executionGroupId: executionGroupId);
        }
    }
}
