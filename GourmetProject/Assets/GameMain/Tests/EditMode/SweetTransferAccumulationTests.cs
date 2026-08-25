using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using GourmetProject.Core.Rng;
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
    }
}
