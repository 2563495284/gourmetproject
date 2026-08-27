using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SweetTransferAccumulationTests
    {
        [Test]
        public void NewlyReceivedSkill_IsIncludedInSkillCountDuringCurrentSettlement()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef sourceSkill = CreateSkillCountTransferSkill("source_skill", 10f);
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetDef = CreateDish("target", "目标", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(2, 1);
            DishInstance source = CreateInstance(1, sourceDef, shape, 0);
            DishInstance target = CreateInstance(2, targetDef, shape, 1);
            board.Place(source);
            board.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) => new[] { target.Id });

            DishScore targetScore = result.DishScores.Single(score => score.DishInstanceId == target.Id);
            Assert.That(Value(targetScore.FlatBonus), Is.EqualTo(10d).Within(1e-9));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(result.SkillTransfers[0].HandoffExecutionGroupId, Is.GreaterThan(0));
            Assert.That(target.TransferredSkills, Is.Empty, "预览计算不得提前提交传递技能");
        }

        [Test]
        public void RepeatedReceives_ReplayAllCommittedAndPendingSkillsWithOriginalSources()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillRuleDef oldRule = CreateAddFlatRule("old_payload", "old_skill", 1f);
            SkillDef oldSkill = CreatePayloadOnlySkill("old_skill", oldRule);
            SkillDef sourceBSkill = CreateTransferSkill("source_b_skill", 10f);
            SkillDef sourceCSkill = CreateTransferSkill("source_c_skill", 100f);

            DishDef oldOwnerDef = CreateDish("old_owner", "旧来源", shape, Array.Empty<string>());
            DishDef sourceBDef = CreateDish("source_b", "来源B", shape, new[] { sourceBSkill.Id });
            DishDef sourceCDef = CreateDish("source_c", "来源C", shape, new[] { sourceCSkill.Id });
            DishDef targetDef = CreateDish("target", "目标", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { oldOwnerDef, sourceBDef, sourceCDef, targetDef },
                new[] { oldSkill, sourceBSkill, sourceCSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(4, 1);
            DishInstance oldOwner = CreateInstance(1, oldOwnerDef, shape, 0);
            DishInstance sourceB = CreateInstance(2, sourceBDef, shape, 1);
            DishInstance sourceC = CreateInstance(3, sourceCDef, shape, 2);
            DishInstance target = CreateInstance(4, targetDef, shape, 3);
            board.Place(oldOwner);
            board.Place(sourceB);
            board.Place(sourceC);
            board.Place(target);
            target.AddTransferredSkill(
                new SkillEffect(oldRule, "旧技能"),
                "旧来源<甜蜜传递>",
                oldOwner.Id);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) =>
                    candidates.Contains(target.Id)
                        ? new[] { target.Id }
                        : candidates.Take(count).ToArray());

            DishScore targetScore = result.DishScores.Single(score => score.DishInstanceId == target.Id);
            Assert.That(Value(targetScore.FlatBonus), Is.EqualTo(123d).Within(1e-9));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(2));
            Assert.That(result.SkillTransfers.All(transfer => transfer.TargetInstanceId == target.Id), Is.True);
            Assert.That(result.SkillTransfers.All(transfer => transfer.Effects.Count == 1), Is.True);
            Assert.That(target.TransferredSkills, Has.Count.EqualTo(1), "预览计算不得修改运行时技能集");

            IReadOnlyList<ScoreLine> transferLines = result.ScoreLines
                .Where(line => line.DishInstanceId == target.Id
                    && line.Kind == ScoreLineKind.DishFlat
                    && line.Trace?.Kind == SkillExecutionKind.SweetTransfer)
                .ToArray();
            Assert.That(transferLines.Count(line => line.Trace.SourceLabel == "旧来源<甜蜜传递>"), Is.EqualTo(3));
            Assert.That(transferLines.Count(line => line.Trace.SourceLabel == "来源B<甜蜜传递>"), Is.EqualTo(2));
            Assert.That(transferLines.Count(line => line.Trace.SourceLabel == "来源C<甜蜜传递>"), Is.EqualTo(1));
            Assert.That(
                transferLines.Where(line => line.Trace.SourceLabel == "旧来源<甜蜜传递>")
                    .All(line => line.Trace.OwnerDishInstanceId == oldOwner.Id),
                Is.True);
            Assert.That(
                transferLines.Where(line => line.Trace.SourceLabel == "来源B<甜蜜传递>")
                    .All(line => line.Trace.OwnerDishInstanceId == sourceB.Id),
                Is.True);
            Assert.That(
                transferLines.Where(line => line.Trace.SourceLabel == "来源C<甜蜜传递>")
                    .All(line => line.Trace.OwnerDishInstanceId == sourceC.Id),
                Is.True);

            Assert.That(
                transferLines.Count(line => line.Trace.SweetTransferHandoffSourceDishInstanceId == 0),
                Is.EqualTo(1),
                "目标正常结算旧技能时没有新的传递来源");
            IReadOnlyList<ScoreLine> sourceBHandoffLines = transferLines
                .Where(line => line.Trace.SweetTransferHandoffSourceDishInstanceId == sourceB.Id)
                .ToArray();
            IReadOnlyList<ScoreLine> sourceCHandoffLines = transferLines
                .Where(line => line.Trace.SweetTransferHandoffSourceDishInstanceId == sourceC.Id)
                .ToArray();
            Assert.That(sourceBHandoffLines.Count, Is.EqualTo(2));
            Assert.That(sourceCHandoffLines.Count, Is.EqualTo(3));
            Assert.That(
                sourceBHandoffLines.Select(line => line.Trace.SweetTransferHandoffExecutionGroupId).Distinct().Count(),
                Is.EqualTo(1));
            Assert.That(
                sourceCHandoffLines.Select(line => line.Trace.SweetTransferHandoffExecutionGroupId).Distinct().Count(),
                Is.EqualTo(1));
            Assert.That(
                sourceBHandoffLines[0].Trace.SweetTransferHandoffExecutionGroupId,
                Is.Not.EqualTo(sourceCHandoffLines[0].Trace.SweetTransferHandoffExecutionGroupId));
            foreach (SkillTransferSideEffect transfer in result.SkillTransfers)
            {
                int expectedGroupId = transfer.SourceInstanceId == sourceB.Id
                    ? sourceBHandoffLines[0].Trace.SweetTransferHandoffExecutionGroupId
                    : sourceCHandoffLines[0].Trace.SweetTransferHandoffExecutionGroupId;
                Assert.That(transfer.HandoffExecutionGroupId, Is.EqualTo(expectedGroupId));
            }

            var sourceCGroups = sourceCHandoffLines
                .Select(line => new SettlementEffectGroup(line))
                .ToArray();
            IReadOnlyList<SettlementSweetTransferPresentationContext> sourceCHandoffs =
                SettlementSequencer.CollectWaveHandoffs(sourceCGroups, 0, sourceCGroups.Length);
            Assert.That(sourceCHandoffs.Count, Is.EqualTo(1));
            Assert.That(sourceCHandoffs[0].SourceDishInstanceId, Is.EqualTo(sourceC.Id));
            Assert.That(sourceCHandoffs[0].EffectOwnerDishInstanceId, Is.EqualTo(sourceC.Id));
            Assert.That(sourceCHandoffs[0].ExecutorDishInstanceId, Is.EqualTo(target.Id));
            Assert.That(sourceCHandoffs[0].HandoffSkillId, Is.EqualTo(sourceCSkill.Id));
            Assert.That(sourceCHandoffs[0].HandoffPayloadCount, Is.EqualTo(1));
        }

        [Test]
        public void PresentationHandoffs_GroupByActualTransferExecutionAndReceiver()
        {
            const int sourceBId = 2;
            const int sourceCId = 3;
            const int targetAId = 4;
            const int targetDId = 5;

            ScoreLine oldEffectDuringC = CreatePresentationLine(
                ownerId: sourceBId,
                executorId: targetAId,
                skillId: "source_b_skill",
                handoffSourceId: sourceCId,
                handoffGroupId: 101);
            ScoreLine newEffectDuringC = CreatePresentationLine(
                ownerId: sourceCId,
                executorId: targetAId,
                skillId: "source_c_skill",
                handoffSourceId: sourceCId,
                handoffGroupId: 101);
            ScoreLine sameTransferOtherTarget = CreatePresentationLine(
                ownerId: sourceCId,
                executorId: targetDId,
                skillId: "source_c_skill",
                handoffSourceId: sourceCId,
                handoffGroupId: 101);
            ScoreLine repeatedTransferSameTarget = CreatePresentationLine(
                ownerId: sourceCId,
                executorId: targetAId,
                skillId: "source_c_skill",
                handoffSourceId: sourceCId,
                handoffGroupId: 102);

            var groups = new[]
            {
                new SettlementEffectGroup(oldEffectDuringC),
                new SettlementEffectGroup(newEffectDuringC),
                new SettlementEffectGroup(sameTransferOtherTarget),
                new SettlementEffectGroup(repeatedTransferSameTarget),
            };

            IReadOnlyList<SettlementSweetTransferPresentationContext> handoffs =
                SettlementSequencer.CollectWaveHandoffs(groups, 0, groups.Length);

            Assert.That(handoffs.Count, Is.EqualTo(3));
            Assert.That(
                handoffs.Count(context => context.SourceDishInstanceId == sourceCId
                    && context.ExecutorDishInstanceId == targetAId
                    && context.HandoffExecutionGroupId == 101),
                Is.EqualTo(1),
                "同一次 C→A 的历史与新增技能只能生成一个交接");
            Assert.That(
                handoffs.Any(context => context.ExecutorDishInstanceId == targetDId
                    && context.HandoffExecutionGroupId == 101),
                Is.True,
                "同一次传递的其它接收者需要自己的粒子");
            Assert.That(
                handoffs.Any(context => context.ExecutorDishInstanceId == targetAId
                    && context.HandoffExecutionGroupId == 102),
                Is.True,
                "同一来源对同一目标的下一次真实传递不能被合并");
        }

        [Test]
        public void PassivePresentationLedger_ConsumesExactTransfersAndBatchesEachWave()
        {
            const string itemId = "item_gold_on_transfer";
            const int sourceId = 3;
            const int targetAId = 4;
            const int targetBId = 5;
            ScoreLine firstTarget = CreatePresentationLine(
                ownerId: sourceId,
                executorId: targetAId,
                skillId: "source_skill",
                handoffSourceId: sourceId,
                handoffGroupId: 101);
            ScoreLine secondTarget = CreatePresentationLine(
                ownerId: sourceId,
                executorId: targetBId,
                skillId: "source_skill",
                handoffSourceId: sourceId,
                handoffGroupId: 101);
            ScoreLine repeatedTarget = CreatePresentationLine(
                ownerId: sourceId,
                executorId: targetAId,
                skillId: "source_skill",
                handoffSourceId: sourceId,
                handoffGroupId: 102);

            var ledger = new PassiveSettlementPlaybackLedger(new[]
            {
                new PassiveSettlementPresentationOccurrence(
                    itemId,
                    new SweetTransferOccurrence(sourceId, targetAId, 101),
                    "8",
                    "9",
                    0,
                    false),
                new PassiveSettlementPresentationOccurrence(
                    itemId,
                    new SweetTransferOccurrence(sourceId, targetBId, 101),
                    "9",
                    "0",
                    15,
                    true),
                new PassiveSettlementPresentationOccurrence(
                    itemId,
                    new SweetTransferOccurrence(sourceId, targetAId, 102),
                    "0",
                    "1",
                    0,
                    false),
            });
            var firstWaveGroups = new[]
            {
                new SettlementEffectGroup(firstTarget),
                new SettlementEffectGroup(secondTarget),
            };
            IReadOnlyList<SettlementSweetTransferPresentationContext> firstWave =
                SettlementSequencer.CollectWaveHandoffs(
                    firstWaveGroups,
                    0,
                    firstWaveGroups.Length);

            IReadOnlyList<PassiveSettlementPresentationBatch> firstBatches =
                ledger.ConsumeWave(firstWave);

            Assert.That(firstBatches.Count, Is.EqualTo(1));
            Assert.That(firstBatches[0].ItemId, Is.EqualTo(itemId));
            Assert.That(firstBatches[0].InfoTextBefore, Is.EqualTo("8"));
            Assert.That(firstBatches[0].InfoTextAfter, Is.EqualTo("0"));
            Assert.That(firstBatches[0].GoldDelta, Is.EqualTo(15));
            Assert.That(firstBatches[0].ShouldPulse, Is.True);
            Assert.That(ledger.UnconsumedCount, Is.EqualTo(1));
            Assert.That(ledger.ConsumeWave(firstWave), Is.Empty, "已抵达的传递不能重复消费");

            var repeatedGroups = new[] { new SettlementEffectGroup(repeatedTarget) };
            IReadOnlyList<PassiveSettlementPresentationBatch> repeatedBatches =
                ledger.ConsumeWave(SettlementSequencer.CollectWaveHandoffs(
                    repeatedGroups,
                    0,
                    repeatedGroups.Length));

            Assert.That(repeatedBatches.Count, Is.EqualTo(1));
            Assert.That(repeatedBatches[0].InfoTextAfter, Is.EqualTo("1"));
            Assert.That(repeatedBatches[0].GoldDelta, Is.Zero);
            Assert.That(repeatedBatches[0].ShouldPulse, Is.False);
            Assert.That(ledger.UnconsumedCount, Is.Zero);
        }

        [TestCase(9, 1, 15, "0", true)]
        [TestCase(8, 1, 0, "9", false)]
        [TestCase(8, 3, 15, "1", true)]
        [TestCase(9, 11, 30, "0", true)]
        public void FormalSettlement_BrassBellAppliesImmediatelyAndRecordsDeferredPresentation(
            int initialCount,
            int targetCount,
            int expectedGoldDelta,
            string expectedFinalInfo,
            bool expectedPulse)
        {
            (GameRun run, GoldOnTransferCountModel bell) = CreateBrassBellRun(initialCount);

            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef sourceSkill = CreateTransferSkill("source_skill", 10f, targetCount);
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            var dishDefs = new List<DishDef> { sourceDef };
            for (int i = 0; i < targetCount; i++)
            {
                dishDefs.Add(CreateDish(
                    $"target_{i}",
                    $"目标{i}",
                    shape,
                    Array.Empty<string>()));
            }

            var database = new GameplayDatabase(
                dishDefs,
                new[] { sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(dishDefs.Count, 1);
            for (int i = 0; i < dishDefs.Count; i++)
            {
                board.Place(CreateInstance(i + 1, dishDefs[i], shape, i));
            }

            var session = new BattleSession(
                board,
                database,
                new Xoshiro256SS(20260826UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
            bell.ApplyToBattle(session);
            int goldBefore = run.Gold;

            ScoreResult settled = session.Settle();

            Assert.That(run.Gold - goldBefore, Is.EqualTo(expectedGoldDelta));
            Assert.That(bell.InfoText, Is.EqualTo(expectedFinalInfo));
            Assert.That(bell.CaptureState(), Is.EqualTo($"count:{expectedFinalInfo}"));
            Assert.That(
                session.PassiveSettlementPresentationOccurrences,
                Has.Count.EqualTo(targetCount));
            Assert.That(
                session.PassiveSettlementPresentationOccurrences.Sum(entry => entry.GoldDelta),
                Is.EqualTo(expectedGoldDelta));
            Assert.That(
                session.PassiveSettlementPresentationOccurrences.Any(entry => entry.ShouldPulse),
                Is.EqualTo(expectedPulse));
            Assert.That(
                session.PassiveSettlementPresentationOccurrences.All(entry =>
                    entry.Transfer.HandoffExecutionGroupId > 0),
                Is.True);

            SettlementEffectGroup[] transferGroups = settled.ScoreLines
                .Where(line => line.Trace?.SweetTransferHandoffExecutionGroupId > 0)
                .Select(line => new SettlementEffectGroup(line))
                .ToArray();
            IReadOnlyList<SettlementSweetTransferPresentationContext> handoffs =
                SettlementSequencer.CollectWaveHandoffs(
                    transferGroups,
                    0,
                    transferGroups.Length);
            IReadOnlyList<PassiveSettlementPresentationBatch> batches =
                new PassiveSettlementPlaybackLedger(
                    session.PassiveSettlementPresentationOccurrences)
                .ConsumeWave(handoffs);
            Assert.That(batches.Count, Is.EqualTo(1));
            Assert.That(batches[0].InfoTextBefore, Is.EqualTo(initialCount.ToString()));
            Assert.That(batches[0].InfoTextAfter, Is.EqualTo(expectedFinalInfo));
            Assert.That(batches[0].GoldDelta, Is.EqualTo(expectedGoldDelta));
            Assert.That(batches[0].ShouldPulse, Is.EqualTo(expectedPulse));
        }

        private static (GameRun Run, GoldOnTransferCountModel Bell) CreateBrassBellRun(
            int initialCount)
        {
            const string definitionJson =
                "{"
                + "\"id\":\"item_gold_on_transfer\","
                + "\"name\":\"黄铜传菜铃\","
                + "\"desc\":\"\","
                + "\"quality\":0,"
                + "\"specialTags\":0,"
                + "\"effectValue\":15,"
                + "\"effectParam\":\"count:10\","
                + "\"baseWeight\":100,"
                + "\"targetScoreHiddenOffset\":0,"
                + "\"dishHiddenOffset\":0,"
                + "\"itemLuckOffset\":0,"
                + "\"fragmentHiddenOffset\":0,"
                + "\"termId\":\"term_sweet_transfer\","
                + "\"price\":60,"
                + "\"archetypeTags\":[0]"
                + "}";
            var state = new RunItemState("item_gold_on_transfer", 1);
            var items = new List<RunItemState> { state };
#pragma warning disable SYSLIB0050
            var run = (GameRun)FormatterServices.GetUninitializedObject(typeof(GameRun));
#pragma warning restore SYSLIB0050
            typeof(GameRun)
                .GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(run, items);
            var bell = new GoldOnTransferCountModel();
            bell.Bind(
                run,
                ItemDefinition.From(new cfg.PassiveItem(JSON.Parse(definitionJson))),
                state);
            state.Model = bell;
            bell.RestoreState($"count:{initialCount}");
            return (run, bell);
        }

        [Test]
        public void TriggerSweetTransfer_UsesTriggeredSourceAsHandoffSource()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef triggerSkill = CreateTriggerTransferSkill("trigger_skill");
            SkillDef sourceSkill = CreateTransferSkill("source_skill", 10f);
            DishDef activatorDef = CreateDish("activator", "代触发者", shape, new[] { triggerSkill.Id });
            DishDef sourceDef = CreateDish("source", "真实来源", shape, new[] { sourceSkill.Id });
            DishDef targetDef = CreateDish("target", "目标", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { activatorDef, sourceDef, targetDef },
                new[] { triggerSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(3, 1);
            DishInstance activator = CreateInstance(1, activatorDef, shape, 0);
            DishInstance source = CreateInstance(2, sourceDef, shape, 1);
            DishInstance target = CreateInstance(3, targetDef, shape, 2);
            board.Place(activator);
            board.Place(source);
            board.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) =>
                    candidates.Contains(target.Id)
                        ? new[] { target.Id }
                        : candidates.Take(count).ToArray());

            Assert.That(
                result.ScoreLines.Any(line => line.Kind == ScoreLineKind.TriggerSweetTransfer
                    && line.DishInstanceId == activator.Id),
                Is.True);
            IReadOnlyList<ScoreLine> transferredLines = result.ScoreLines
                .Where(line => line.DishInstanceId == target.Id
                    && line.Trace?.Kind == SkillExecutionKind.SweetTransfer
                    && line.Trace.SweetTransferHandoffExecutionGroupId > 0)
                .ToArray();
            Assert.That(transferredLines, Is.Not.Empty);
            Assert.That(
                transferredLines.All(line => line.Trace.OwnerDishInstanceId == source.Id),
                Is.True);
            Assert.That(
                transferredLines.All(line =>
                    line.Trace.SweetTransferHandoffSourceDishInstanceId == source.Id),
                Is.True);
            Assert.That(
                transferredLines.All(line =>
                    line.Trace.SweetTransferHandoffSourceDishInstanceId != activator.Id),
                Is.True);
        }

        [TestCase(1, 0, 2)]
        [TestCase(1, 9999, 1)]
        [TestCase(5, 0, 6)]
        [TestCase(5, 9999, 5)]
        public void ChanceExtraTarget_RollsOnceRegardlessOfBaseTargetCount(
            int baseTargetCount,
            int roll,
            int expectedTransferCount)
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount,
                chanceModifierCount: 1,
                fixedExtraTargetCount: 0,
                plainTargetCount: 6);
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return roll;
                });

            Assert.That(randomCallCount, Is.EqualTo(1));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(expectedTransferCount));
        }

        [TestCase(0, 0, 7)]
        [TestCase(0, 9999, 6)]
        [TestCase(9999, 9999, 5)]
        public void MultipleChanceExtraTargets_EachRollOnceAndAccumulate(
            int firstRoll,
            int secondRoll,
            int expectedTransferCount)
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount: 5,
                chanceModifierCount: 2,
                fixedExtraTargetCount: 0,
                plainTargetCount: 6);
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return randomCallCount == 1 ? firstRoll : secondRoll;
                });

            Assert.That(randomCallCount, Is.EqualTo(2));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(expectedTransferCount));
        }

        [Test]
        public void FixedExtraTargets_DoNotIncreaseChanceRollCount()
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount: 1,
                chanceModifierCount: 1,
                fixedExtraTargetCount: 2,
                plainTargetCount: 5);
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return 0;
                },
                sweetTransferExtraTargetCount: 2);

            Assert.That(randomCallCount, Is.EqualTo(1));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(6));
        }

        [TestCase(0, 1)]
        [TestCase(6999, 1)]
        [TestCase(7000, 2)]
        [TestCase(9999, 2)]
        public void WeightedItemExtraTargets_UsesSeventyThirtyThreshold(
            int roll,
            int expectedExtraTargetCount)
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount: 1,
                chanceModifierCount: 0,
                fixedExtraTargetCount: 0,
                plainTargetCount: 4);
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) =>
                {
                    Assert.That(min, Is.EqualTo(0));
                    Assert.That(max, Is.EqualTo(9999));
                    randomCallCount++;
                    return roll;
                },
                sweetTransferExtraTargetRolls: TowerExtraTargetRolls());

            Assert.That(randomCallCount, Is.EqualTo(1));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(1 + expectedExtraTargetCount));
        }

        [Test]
        public void WeightedItemExtraTargets_PurePreviewUsesMostLikelyChoiceAndStacksWithFixedTargets()
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount: 1,
                chanceModifierCount: 0,
                fixedExtraTargetCount: 0,
                plainTargetCount: 5);

            ScoreResult preview = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                sweetTransferExtraTargetCount: 2,
                sweetTransferExtraTargetRolls: TowerExtraTargetRolls());

            Assert.That(preview.SkillTransfers, Has.Count.EqualTo(4));
        }

        [Test]
        public void WeightedItemExtraTargets_TriggeredAndNativeTransfersRollIndependently()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef triggerSkill = CreateTriggerTransferSkill("trigger_skill");
            SkillDef sourceSkill = CreateTransferSkill("source_skill", 10f);
            DishDef triggerDef = CreateDish("trigger", "代触发者", shape, new[] { triggerSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetADef = CreateDish("target_a", "目标A", shape, Array.Empty<string>());
            DishDef targetBDef = CreateDish("target_b", "目标B", shape, Array.Empty<string>());
            DishDef targetCDef = CreateDish("target_c", "目标C", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { triggerDef, sourceDef, targetADef, targetBDef, targetCDef },
                new[] { triggerSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(5, 1);
            board.Place(CreateInstance(1, triggerDef, shape, 0));
            board.Place(CreateInstance(2, sourceDef, shape, 1));
            board.Place(CreateInstance(3, targetADef, shape, 2));
            board.Place(CreateInstance(4, targetBDef, shape, 3));
            board.Place(CreateInstance(5, targetCDef, shape, 4));
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) => randomCallCount++ == 0 ? 0 : 7000,
                sweetTransferExtraTargetRolls: TowerExtraTargetRolls());

            Assert.That(randomCallCount, Is.EqualTo(2));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(5));
        }

        [Test]
        public void WeightedItemExtraTargets_OnServeUsesTheSameThreshold()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef sourceSkill = CreateTransferSkill(
                "source_skill",
                value: 10f,
                targetCount: 1,
                trigger: SkillTrigger.OnServe);
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetADef = CreateDish("target_a", "目标A", shape, Array.Empty<string>());
            DishDef targetBDef = CreateDish("target_b", "目标B", shape, Array.Empty<string>());
            DishDef targetCDef = CreateDish("target_c", "目标C", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { sourceDef, targetADef, targetBDef, targetCDef },
                new[] { sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(4, 1);
            DishInstance source = CreateInstance(1, sourceDef, shape, 0);
            board.Place(source);
            board.Place(CreateInstance(2, targetADef, shape, 1));
            board.Place(CreateInstance(3, targetBDef, shape, 2));
            board.Place(CreateInstance(4, targetCDef, shape, 3));
            int randomCallCount = 0;

            ServeRuleResolver.ServeResolveResult result = ServeRuleResolver.ResolveOnServe(
                board,
                database,
                EmptyScoreHistory.Instance,
                source,
                currentHappyCakeLayers: 0,
                itemExtraTargetRolls: TowerExtraTargetRolls(),
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return 7000;
                });

            Assert.That(randomCallCount, Is.EqualTo(1));
            Assert.That(result.TransferRequests, Has.Count.EqualTo(1));
            Assert.That(result.TransferRequests[0].Count, Is.EqualTo(3));
        }

        [Test]
        public void WeightedItemExtraTargets_FailedOrEmptyTransferDoesNotConsumeRandom()
        {
            ChanceModifierFixture noCandidates = CreateChanceModifierFixture(
                baseTargetCount: 1,
                chanceModifierCount: 0,
                fixedExtraTargetCount: 0,
                plainTargetCount: 0);
            int randomCallCount = 0;

            ScoreResult failed = new ScoreCalculator().Calculate(
                noCandidates.Board,
                noCandidates.Database,
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return 0;
                },
                sweetTransferExtraTargetRolls: TowerExtraTargetRolls());

            Assert.That(failed.SkillTransfers, Is.Empty);
            Assert.That(randomCallCount, Is.EqualTo(0));

            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef emptyTransferSkill = CreateEmptyTransferSkill("empty_transfer");
            DishDef sourceDef = CreateDish("empty_source", "空载来源", shape, new[] { emptyTransferSkill.Id });
            DishDef targetDef = CreateDish("target", "目标", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { emptyTransferSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(2, 1);
            board.Place(CreateInstance(1, sourceDef, shape, 0));
            board.Place(CreateInstance(2, targetDef, shape, 1));

            ScoreResult empty = new ScoreCalculator().Calculate(
                board,
                database,
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return 0;
                },
                sweetTransferExtraTargetRolls: TowerExtraTargetRolls());

            Assert.That(empty.SkillTransfers, Is.Empty);
            Assert.That(randomCallCount, Is.EqualTo(0));
        }

        [Test]
        public void ChanceExtraTarget_IsCappedByLegalCandidates()
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount: 5,
                chanceModifierCount: 1,
                fixedExtraTargetCount: 0,
                plainTargetCount: 1);
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return 0;
                });

            Assert.That(randomCallCount, Is.EqualTo(1));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(2));
        }

        [Test]
        public void ChanceExtraTarget_IsNotGrantedWithoutRandomSelector()
        {
            ChanceModifierFixture fixture = CreateChanceModifierFixture(
                baseTargetCount: 1,
                chanceModifierCount: 1,
                fixedExtraTargetCount: 0,
                plainTargetCount: 2);

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray());

            Assert.That(result.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(
                result.ScoreLines.Any(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered),
                Is.False);
        }

        [Test]
        public void TriggeredAndNativeTransfers_EachRollChanceExtraTargetOnce()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef modifierSkill = CreateExtraTargetModifierSkill(
                "chance_modifier",
                extraTargetCount: 1,
                probabilistic: true);
            SkillDef triggerSkill = CreateTriggerTransferSkill("trigger_skill");
            SkillDef sourceSkill = CreateTransferSkill("source_skill", 10f);
            DishDef modifierDef = CreateDish("modifier", "松露", shape, new[] { modifierSkill.Id });
            DishDef triggerDef = CreateDish("trigger", "代触发者", shape, new[] { triggerSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetADef = CreateDish("target_a", "目标A", shape, Array.Empty<string>());
            DishDef targetBDef = CreateDish("target_b", "目标B", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { modifierDef, triggerDef, sourceDef, targetADef, targetBDef },
                new[] { modifierSkill, triggerSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(5, 1);
            board.Place(CreateInstance(1, modifierDef, shape, 0));
            board.Place(CreateInstance(2, triggerDef, shape, 1));
            board.Place(CreateInstance(3, sourceDef, shape, 2));
            board.Place(CreateInstance(4, targetADef, shape, 3));
            board.Place(CreateInstance(5, targetBDef, shape, 4));
            int randomCallCount = 0;

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) => candidates.Take(count).ToArray(),
                randomIntegerSelector: (min, max) =>
                {
                    randomCallCount++;
                    return 0;
                });

            Assert.That(randomCallCount, Is.EqualTo(2));
            Assert.That(result.SkillTransfers, Has.Count.EqualTo(4));
        }

        [Test]
        public void TransferMultiplierModifier_ScalesByActualSuccessfulTargetCount()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef modifierSkill = CreateTransferMultiplierModifierSkill(
                "gummy_modifier",
                scaleByTransferTargetCount: true,
                scope: SkillScope.ColumnAndSelf);
            SkillDef sourceSkill = CreateTransferSkill("source_skill", value: 10f, targetCount: 2);
            DishDef modifierDef = CreateDish("modifier", "软糖", shape, new[] { modifierSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetADef = CreateDish("target_a", "目标A", shape, Array.Empty<string>());
            DishDef targetBDef = CreateDish("target_b", "目标B", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { modifierDef, sourceDef, targetADef, targetBDef },
                new[] { modifierSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(3, 2);
            DishInstance modifier = CreateInstance(1, modifierDef, shape, 0, 0);
            DishInstance source = CreateInstance(2, sourceDef, shape, 0, 1);
            DishInstance targetA = CreateInstance(3, targetADef, shape, 1, 0);
            DishInstance targetB = CreateInstance(4, targetBDef, shape, 2, 0);
            board.Place(modifier);
            board.Place(source);
            board.Place(targetA);
            board.Place(targetB);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) => new[] { targetA.Id, targetB.Id });

            Assert.That(result.SkillTransfers, Has.Count.EqualTo(2));
            Assert.That(Multiplier(result, modifier), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(Multiplier(result, source), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(Multiplier(result, targetA), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Multiplier(result, targetB), Is.EqualTo(1d).Within(1e-9));
            IReadOnlyList<ScoreLine> responseLines = result.ScoreLines
                .Where(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered)
                .ToArray();
            Assert.That(responseLines.Count, Is.EqualTo(1), "多目标传递只生成一条汇总响应");
            Assert.That(Value(responseLines[0].Value), Is.EqualTo(1.6d).Within(1e-6));
        }

        [Test]
        public void TransferMultiplierModifier_UsesSanitizedTargetsInsteadOfConfiguredCount()
        {
            TransferMultiplierFixture fixture = CreateTransferMultiplierFixture(
                scaleByTransferTargetCount: true,
                configuredTargetCount: 3,
                plainTargetCount: 1);

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) =>
                    new[] { fixture.TargetIds[0], fixture.TargetIds[0], int.MaxValue });

            Assert.That(result.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(Multiplier(result, fixture.Modifier), Is.EqualTo(1.8d).Within(1e-6));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered),
                Is.EqualTo(1));
        }

        [Test]
        public void TransferMultiplierModifier_WithoutScaleParamKeepsSingleActivationValue()
        {
            TransferMultiplierFixture fixture = CreateTransferMultiplierFixture(
                scaleByTransferTargetCount: false,
                configuredTargetCount: 2,
                plainTargetCount: 2);

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => fixture.TargetIds);

            Assert.That(result.SkillTransfers, Has.Count.EqualTo(2));
            Assert.That(Multiplier(result, fixture.Modifier), Is.EqualTo(1.8d).Within(1e-6));
            ScoreLine response = result.ScoreLines.Single(
                line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered);
            Assert.That(Value(response.Value), Is.EqualTo(0.8d).Within(1e-6));
        }

        [Test]
        public void TransferMultiplierModifier_EmptySelectionDoesNotTrigger()
        {
            TransferMultiplierFixture fixture = CreateTransferMultiplierFixture(
                scaleByTransferTargetCount: true,
                configuredTargetCount: 2,
                plainTargetCount: 2);

            ScoreResult result = new ScoreCalculator().Calculate(
                fixture.Board,
                fixture.Database,
                transferTargetSelector: (candidates, count) => Array.Empty<int>());

            Assert.That(result.SkillTransfers, Is.Empty);
            Assert.That(Multiplier(result, fixture.Modifier), Is.EqualTo(1d).Within(1e-9));
            Assert.That(
                result.ScoreLines.Any(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered),
                Is.False);
        }

        [Test]
        public void TransferMultiplierModifier_IncludesFixedAndProbabilisticExtraTargets()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef multiplierSkill = CreateTransferMultiplierModifierSkill(
                "gummy_modifier",
                scaleByTransferTargetCount: true);
            SkillDef chanceSkill = CreateExtraTargetModifierSkill(
                "chance_modifier",
                extraTargetCount: 1,
                probabilistic: true);
            SkillDef sourceSkill = CreateTransferSkill("source_skill", value: 10f, targetCount: 1);
            DishDef multiplierDef = CreateDish("multiplier", "软糖", shape, new[] { multiplierSkill.Id });
            DishDef chanceDef = CreateDish("chance", "松露", shape, new[] { chanceSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetADef = CreateDish("target_a", "目标A", shape, Array.Empty<string>());
            DishDef targetBDef = CreateDish("target_b", "目标B", shape, Array.Empty<string>());
            DishDef targetCDef = CreateDish("target_c", "目标C", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { multiplierDef, chanceDef, sourceDef, targetADef, targetBDef, targetCDef },
                new[] { multiplierSkill, chanceSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(6, 1);
            DishInstance multiplier = CreateInstance(1, multiplierDef, shape, 0);
            DishInstance targetA = CreateInstance(4, targetADef, shape, 3);
            DishInstance targetB = CreateInstance(5, targetBDef, shape, 4);
            DishInstance targetC = CreateInstance(6, targetCDef, shape, 5);
            board.Place(multiplier);
            board.Place(CreateInstance(2, chanceDef, shape, 1));
            board.Place(CreateInstance(3, sourceDef, shape, 2));
            board.Place(targetA);
            board.Place(targetB);
            board.Place(targetC);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) =>
                    new[] { targetA.Id, targetB.Id, targetC.Id }.Take(count).ToArray(),
                randomIntegerSelector: (min, max) => 0,
                sweetTransferExtraTargetCount: 1);

            Assert.That(result.SkillTransfers, Has.Count.EqualTo(3));
            Assert.That(Multiplier(result, multiplier), Is.EqualTo(3.4d).Within(1e-6));
            ScoreLine response = result.ScoreLines.Single(line =>
                line.Kind == ScoreLineKind.SweetTransferBuffTriggered
                && line.DishInstanceId == multiplier.Id);
            Assert.That(Value(response.Value), Is.EqualTo(2.4d).Within(1e-6));
        }

        [Test]
        public void TransferMultiplierModifier_TriggeredAndNativeTransfersAccumulateSeparately()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef modifierSkill = CreateTransferMultiplierModifierSkill(
                "gummy_modifier",
                scaleByTransferTargetCount: true);
            SkillDef triggerSkill = CreateTriggerTransferSkill("trigger_skill");
            SkillDef sourceSkill = CreateTransferSkill("source_skill", value: 10f, targetCount: 2);
            DishDef modifierDef = CreateDish("modifier", "软糖", shape, new[] { modifierSkill.Id });
            DishDef triggerDef = CreateDish("trigger", "代触发者", shape, new[] { triggerSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetADef = CreateDish("target_a", "目标A", shape, Array.Empty<string>());
            DishDef targetBDef = CreateDish("target_b", "目标B", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { modifierDef, triggerDef, sourceDef, targetADef, targetBDef },
                new[] { modifierSkill, triggerSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(5, 1);
            DishInstance modifier = CreateInstance(1, modifierDef, shape, 0);
            DishInstance targetA = CreateInstance(4, targetADef, shape, 3);
            DishInstance targetB = CreateInstance(5, targetBDef, shape, 4);
            board.Place(modifier);
            board.Place(CreateInstance(2, triggerDef, shape, 1));
            board.Place(CreateInstance(3, sourceDef, shape, 2));
            board.Place(targetA);
            board.Place(targetB);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) =>
                    candidates.Where(id => id == targetA.Id || id == targetB.Id).Take(count).ToArray());

            Assert.That(result.SkillTransfers, Has.Count.EqualTo(4));
            Assert.That(Multiplier(result, modifier), Is.EqualTo(4.2d).Within(1e-6));
            IReadOnlyList<ScoreLine> responses = result.ScoreLines
                .Where(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered)
                .ToArray();
            Assert.That(responses.Count, Is.EqualTo(2));
            Assert.That(responses.All(line => Math.Abs(Value(line.Value) - 1.6d) < 1e-6), Is.True);
        }

        [Test]
        public void TransferMultiplierModifier_OverlappingRegistrationsStackIndependently()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef modifierASkill = CreateTransferMultiplierModifierSkill(
                "gummy_modifier_a",
                scaleByTransferTargetCount: true);
            SkillDef modifierBSkill = CreateTransferMultiplierModifierSkill(
                "gummy_modifier_b",
                scaleByTransferTargetCount: true);
            SkillDef sourceSkill = CreateTransferSkill("source_skill", value: 10f, targetCount: 1);
            DishDef modifierADef = CreateDish("modifier_a", "软糖A", shape, new[] { modifierASkill.Id });
            DishDef modifierBDef = CreateDish("modifier_b", "软糖B", shape, new[] { modifierBSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetDef = CreateDish("target", "目标", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { modifierADef, modifierBDef, sourceDef, targetDef },
                new[] { modifierASkill, modifierBSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(4, 1);
            DishInstance modifierA = CreateInstance(1, modifierADef, shape, 0);
            DishInstance modifierB = CreateInstance(2, modifierBDef, shape, 1);
            DishInstance source = CreateInstance(3, sourceDef, shape, 2);
            DishInstance target = CreateInstance(4, targetDef, shape, 3);
            board.Place(modifierA);
            board.Place(modifierB);
            board.Place(source);
            board.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(
                board,
                database,
                transferTargetSelector: (candidates, count) => new[] { target.Id });

            Assert.That(Multiplier(result, modifierA), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(Multiplier(result, modifierB), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(Multiplier(result, source), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(Multiplier(result, target), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered),
                Is.EqualTo(2));
        }

        [Test]
        public void TransferMultiplierModifier_PreviewAndSettlementMatch()
        {
            TransferMultiplierFixture fixture = CreateTransferMultiplierFixture(
                scaleByTransferTargetCount: true,
                configuredTargetCount: 2,
                plainTargetCount: 2);
            var session = new BattleSession(
                fixture.Board,
                fixture.Database,
                new Xoshiro256SS(20260827UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            ScoreResult preview = session.PreviewScore();
            ScoreResult settled = session.Settle();

            Assert.That(Multiplier(preview, fixture.Modifier), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(Multiplier(settled, fixture.Modifier), Is.EqualTo(2.6d).Within(1e-6));
            Assert.That(
                settled.ScoreLines.Count(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered),
                Is.EqualTo(1));
        }

        [Test]
        public void GummyConfiguration_UsesColumnScopeValueScaleParamAndFinalDescription()
        {
            string directory = Path.Combine(Application.streamingAssetsPath, "Config");
            var tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(directory, name + ".json"))));
            GameplayDatabase database = GameplayContentBuilder.BuildDatabase(tables);

            cfg.SubSkill configured = tables.TbSubSkill.GetOrDefault("sk_gummy_1");
            Assert.That(configured, Is.Not.Null);
            Assert.That((int)configured.ActionScope, Is.EqualTo((int)SkillScope.ColumnAndSelf));
            Assert.That(configured.ActionValue.Single(), Is.EqualTo(0.8f).Within(1e-6));
            Assert.That(
                configured.ActionParam.Any(param =>
                    param.IndexOf("scale:transfer-target-count", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.True);
            Assert.That(configured.DescTemplate, Does.Contain("每成功传递 1 个目标"));

            SkillDef skill = database.GetSkill("sk_gummy");
            Assert.That(skill, Is.Not.Null);
            Assert.That(skill.Rules, Has.Count.EqualTo(1));
            Assert.That(skill.Rules[0].ActionType, Is.EqualTo(SkillActionType.AddMultFlat));
            Assert.That(skill.Rules[0].ActionScope, Is.EqualTo(SkillScope.ColumnAndSelf));
            Assert.That(skill.Desc, Does.Contain("每成功传递 1 个目标"));
        }

        [Test]
        public void FormalSettlement_CommitsOnlyTheNewSkillOnceAfterPurePreview()
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillRuleDef oldRule = CreateAddFlatRule("old_payload", "old_skill", 1f);
            SkillDef oldSkill = CreatePayloadOnlySkill("old_skill", oldRule);
            SkillDef sourceSkill = CreateTransferSkill("source_skill", 10f);
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { sourceSkill.Id });
            DishDef targetDef = CreateDish("target", "目标", shape, Array.Empty<string>());
            var database = new GameplayDatabase(
                new[] { sourceDef, targetDef },
                new[] { oldSkill, sourceSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(2, 1);
            DishInstance source = CreateInstance(1, sourceDef, shape, 0);
            DishInstance target = CreateInstance(2, targetDef, shape, 1);
            board.Place(source);
            board.Place(target);
            target.AddTransferredSkill(
                new SkillEffect(oldRule, "旧技能"),
                "旧来源<甜蜜传递>",
                sourceInstanceId: 99);
            var session = new BattleSession(
                board,
                database,
                new Xoshiro256SS(20260825UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

            ScoreResult preview = session.PreviewScore();

            Assert.That(preview.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(target.TransferredSkills, Has.Count.EqualTo(1));

            ScoreResult settled = session.Settle();

            Assert.That(settled.SkillTransfers, Has.Count.EqualTo(1));
            Assert.That(target.TransferredSkills, Has.Count.EqualTo(2));
            Assert.That(
                target.TransferredSkills.Select(transferred => transferred.Rule.Id),
                Is.EqualTo(new[] { "old_payload", "source_skill_payload" }));
        }

        private static SkillDef CreatePayloadOnlySkill(string skillId, SkillRuleDef payload)
            => new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { payload },
                new[] { payload.Id });

        private static SkillDef CreateTransferSkill(string skillId, float value)
            => CreateTransferSkill(skillId, value, targetCount: 1);

        private static SkillDef CreateTransferSkill(
            string skillId,
            float value,
            int targetCount,
            SkillTrigger trigger = SkillTrigger.OnSettle)
        {
            SkillRuleDef payload = CreateAddFlatRule($"{skillId}_payload", skillId, value);
            SkillRuleDef transfer = new SkillRuleDef(
                $"{skillId}_transfer",
                skillId,
                order: 1,
                trigger,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.All,
                actionCount: targetCount,
                new[] { 0f },
                Array.Empty<string>());
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { payload, transfer },
                new[] { payload.Id, transfer.Id });
        }

        private static SkillDef CreateEmptyTransferSkill(string skillId)
        {
            var transfer = new SkillRuleDef(
                $"{skillId}_transfer",
                skillId,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.All,
                actionCount: 1,
                new[] { 0f },
                Array.Empty<string>());
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { transfer },
                new[] { transfer.Id });
        }

        private static IReadOnlyList<SweetTransferExtraTargetRollSpec> TowerExtraTargetRolls()
            => new[] { new SweetTransferExtraTargetRollSpec(1, 2, 70, 30) };

        private static SkillDef CreateSkillCountTransferSkill(string skillId, float value)
        {
            var payload = new SkillRuleDef(
                $"{skillId}_payload",
                skillId,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.SkillCount,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                actionCount: 0,
                new[] { value },
                Array.Empty<string>());
            var transfer = new SkillRuleDef(
                $"{skillId}_transfer",
                skillId,
                order: 1,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.TransferSkills,
                SkillScope.All,
                actionCount: 1,
                new[] { 0f },
                Array.Empty<string>());
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { payload, transfer },
                new[] { payload.Id, transfer.Id });
        }

        private static SkillDef CreateTriggerTransferSkill(string skillId)
        {
            var trigger = new SkillRuleDef(
                $"{skillId}_trigger",
                skillId,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.TriggerSweetTransfer,
                SkillScope.All,
                actionCount: 0,
                new[] { 0f },
                Array.Empty<string>());
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { trigger },
                new[] { trigger.Id });
        }

        private static SkillDef CreateExtraTargetModifierSkill(
            string skillId,
            int extraTargetCount,
            bool probabilistic)
        {
            var modifier = new SkillRuleDef(
                $"{skillId}_modifier",
                skillId,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.TriggerSweetTransfer,
                SkillScope.All,
                actionCount: 0,
                new[] { (float)extraTargetCount },
                new[]
                {
                    probabilistic
                        ? "modifier:add-targets;chance:0.6"
                        : "modifier:add-targets",
                });
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { modifier },
                new[] { modifier.Id });
        }

        private static SkillDef CreateTransferMultiplierModifierSkill(
            string skillId,
            bool scaleByTransferTargetCount,
            SkillScope scope = SkillScope.All)
        {
            string actionParam = "when:transfer;resultscope:BuffTargets";
            if (scaleByTransferTargetCount)
            {
                actionParam += ";scale:transfer-target-count";
            }

            var modifier = new SkillRuleDef(
                $"{skillId}_modifier",
                skillId,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddMultFlat,
                scope,
                actionCount: 0,
                new[] { 0.8f },
                new[] { actionParam });
            return new SkillDef(
                skillId,
                skillId,
                string.Empty,
                Array.Empty<string>(),
                new[] { modifier },
                new[] { modifier.Id });
        }

        private static TransferMultiplierFixture CreateTransferMultiplierFixture(
            bool scaleByTransferTargetCount,
            int configuredTargetCount,
            int plainTargetCount)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            SkillDef modifierSkill = CreateTransferMultiplierModifierSkill(
                "gummy_modifier",
                scaleByTransferTargetCount);
            SkillDef transferSkill = CreateTransferSkill(
                "source_skill",
                value: 10f,
                targetCount: configuredTargetCount);
            DishDef modifierDef = CreateDish("modifier", "软糖", shape, new[] { modifierSkill.Id });
            DishDef sourceDef = CreateDish("source", "来源", shape, new[] { transferSkill.Id });
            var dishDefs = new List<DishDef> { modifierDef, sourceDef };
            for (int i = 0; i < plainTargetCount; i++)
            {
                dishDefs.Add(CreateDish(
                    $"target_{i}",
                    $"目标{i}",
                    shape,
                    Array.Empty<string>()));
            }

            var database = new GameplayDatabase(
                dishDefs,
                new[] { modifierSkill, transferSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(dishDefs.Count, 1);
            DishInstance modifier = CreateInstance(1, modifierDef, shape, 0);
            board.Place(modifier);
            board.Place(CreateInstance(2, sourceDef, shape, 1));
            var targetIds = new List<int>(plainTargetCount);
            for (int i = 0; i < plainTargetCount; i++)
            {
                int id = i + 3;
                board.Place(CreateInstance(id, dishDefs[i + 2], shape, i + 2));
                targetIds.Add(id);
            }

            return new TransferMultiplierFixture(board, database, modifier, targetIds);
        }

        private static ChanceModifierFixture CreateChanceModifierFixture(
            int baseTargetCount,
            int chanceModifierCount,
            int fixedExtraTargetCount,
            int plainTargetCount)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var skills = new List<SkillDef>();
            var dishDefs = new List<DishDef>();
            for (int i = 0; i < chanceModifierCount; i++)
            {
                SkillDef skill = CreateExtraTargetModifierSkill(
                    $"chance_modifier_{i}",
                    extraTargetCount: 1,
                    probabilistic: true);
                skills.Add(skill);
                dishDefs.Add(CreateDish(
                    $"chance_owner_{i}",
                    $"松露{i}",
                    shape,
                    new[] { skill.Id }));
            }

            if (fixedExtraTargetCount > 0)
            {
                SkillDef skill = CreateExtraTargetModifierSkill(
                    "fixed_modifier",
                    fixedExtraTargetCount,
                    probabilistic: false);
                skills.Add(skill);
                dishDefs.Add(CreateDish("fixed_owner", "固定增目标", shape, new[] { skill.Id }));
            }

            SkillDef transferSkill = CreateTransferSkill(
                "source_skill",
                value: 10f,
                targetCount: baseTargetCount);
            skills.Add(transferSkill);
            dishDefs.Add(CreateDish("source", "来源", shape, new[] { transferSkill.Id }));
            for (int i = 0; i < plainTargetCount; i++)
            {
                dishDefs.Add(CreateDish(
                    $"target_{i}",
                    $"目标{i}",
                    shape,
                    Array.Empty<string>()));
            }

            var database = new GameplayDatabase(
                dishDefs,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var board = new DiningTable(dishDefs.Count, 1);
            for (int i = 0; i < dishDefs.Count; i++)
            {
                board.Place(CreateInstance(i + 1, dishDefs[i], shape, i));
            }

            return new ChanceModifierFixture(board, database);
        }

        private static ScoreLine CreatePresentationLine(
            int ownerId,
            int executorId,
            string skillId,
            int handoffSourceId,
            int handoffGroupId)
        {
            var trace = new SkillExecutionTrace(
                SkillExecutionKind.SweetTransfer,
                ownerId,
                $"dish_{ownerId}",
                $"来源{ownerId}",
                executorId,
                $"dish_{executorId}",
                $"目标{executorId}",
                skillId,
                skillId,
                $"{skillId}_rule",
                0,
                SkillTrigger.OnSettle,
                SkillActionType.AddFlat,
                SkillConditionType.None,
                SkillScope.Self,
                SkillScope.Self,
                $"来源{ownerId}<甜蜜传递>",
                sweetTransferHandoffSourceDishInstanceId: handoffSourceId,
                sweetTransferHandoffExecutionGroupId: handoffGroupId);
            return new ScoreLine(
                ScorePhase.DishSkills,
                ScoreLineKind.DishFlat,
                ScoreSource.FinalModifier(skillId, skillId),
                executorId,
                $"dish_{executorId}",
                null,
                1f,
                0f,
                1f,
                skillId,
                trace,
                executionGroupId: handoffGroupId + ownerId);
        }

        private static SkillRuleDef CreateAddFlatRule(string id, string skillId, float value)
            => new SkillRuleDef(
                id,
                skillId,
                order: 0,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                SkillActionType.AddFlat,
                SkillScope.Self,
                actionCount: 0,
                new[] { value },
                Array.Empty<string>());

        private static DishDef CreateDish(
            string id,
            string name,
            DishShape shape,
            IReadOnlyList<string> skillIds)
            => new DishDef(
                id,
                name,
                deliciousness: 1,
                shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds,
                flavorId: string.Empty);

        private static DishInstance CreateInstance(
            int id,
            DishDef def,
            DishShape shape,
            int x)
            => CreateInstance(id, def, shape, x, y: 0);

        private static DishInstance CreateInstance(
            int id,
            DishDef def,
            DishShape shape,
            int x,
            int y)
            => new DishInstance(
                id,
                def,
                new Placement(shape, rotationIndex: 0, new GridPos(x, y)),
                def.SkillIds,
                Array.Empty<string>());

        private static double Value(BigDouble value) => value.ToDouble();

        private static double Multiplier(ScoreResult result, DishInstance dish)
            => Value(result.DishScores.Single(score => score.DishInstanceId == dish.Id).Multiplier);

        private sealed class ChanceModifierFixture
        {
            public ChanceModifierFixture(DiningTable board, GameplayDatabase database)
            {
                Board = board;
                Database = database;
            }

            public DiningTable Board { get; }

            public GameplayDatabase Database { get; }
        }

        private sealed class TransferMultiplierFixture
        {
            public TransferMultiplierFixture(
                DiningTable board,
                GameplayDatabase database,
                DishInstance modifier,
                IReadOnlyList<int> targetIds)
            {
                Board = board;
                Database = database;
                Modifier = modifier;
                TargetIds = targetIds;
            }

            public DiningTable Board { get; }

            public GameplayDatabase Database { get; }

            public DishInstance Modifier { get; }

            public IReadOnlyList<int> TargetIds { get; }
        }
    }
}
