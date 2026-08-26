using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

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

        private static SkillDef CreateTransferSkill(string skillId, float value, int targetCount)
        {
            SkillRuleDef payload = CreateAddFlatRule($"{skillId}_payload", skillId, value);
            SkillRuleDef transfer = new SkillRuleDef(
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
            => new DishInstance(
                id,
                def,
                new Placement(shape, rotationIndex: 0, new GridPos(x, 0)),
                def.SkillIds,
                Array.Empty<string>());

        private static double Value(BigDouble value) => value.ToDouble();

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
    }
}
