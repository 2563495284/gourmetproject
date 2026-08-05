using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class SweetTransferBuffTests
    {
        [Test]
        public void TransferTargetsIgnoreLegacyDirectionalScopeAndUseAllOtherDishes()
        {
            SkillDef transfer = TransferSkill(
                "transfer",
                "来源技能",
                transferScope: SkillScope.Right);
            DishInstance target = Dish(1, "target", "左侧目标", null, 0, 0);
            DishInstance source = Dish(2, "source", "来源", transfer.Id, 2, 0);
            TestBoard test = BuildBoard(3, 1, target, source);
            IReadOnlyList<int> observedCandidates = Array.Empty<int>();

            ScoreResult result = Calculate(test, new[] { transfer }, (candidates, count) =>
            {
                observedCandidates = candidates.ToArray();
                return new[] { target.Id };
            });

            Assert.That(observedCandidates, Is.EquivalentTo(new[] { target.Id }));
            Assert.That(result.SkillTransfers.Single().TargetInstanceId, Is.EqualTo(target.Id));
            Assert.That(result.ScoreLines, Has.None.Matches<ScoreLine>(line =>
                line.Kind == ScoreLineKind.SweetTransferFailed));
        }

        [Test]
        public void TransferBeforeGummySettlement_DoesNotTriggerUnregisteredBuff()
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能");
            SkillDef gummy = GummySkill();
            DishInstance source = Dish(1, "source", "来源", transfer.Id, 0, 0);
            DishInstance target = Dish(2, "target", "目标", null, 1, 0);
            DishInstance gummyDish = Dish(3, "gummy", "软糖", gummy.Id, 0, 1);
            TestBoard test = BuildBoard(2, 2, source, target, gummyDish);

            ScoreResult result = Calculate(test, transfer, gummy,
                (candidates, count) => new[] { target.Id });

            Assert.That(result.ScoreLines.Count(line => line.Kind == ScoreLineKind.SweetTransferBuffApplied), Is.EqualTo(1));
            Assert.That(result.ScoreLines, Has.None.Matches<ScoreLine>(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered));
            Assert.That(ScoreOf(result, gummyDish).Multiplier, Is.EqualTo(1f));
        }

        [Test]
        public void SuccessfulTransferAfterGummySettlement_MultipliesGummyRowIncludingSelf()
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能");
            SkillDef gummy = GummySkill();
            DishInstance gummyDish = Dish(1, "gummy", "软糖", gummy.Id, 0, 0);
            DishInstance rowMate = Dish(2, "row_mate", "同行", null, 1, 0);
            DishInstance source = Dish(3, "source", "来源", transfer.Id, 0, 1);
            DishInstance receiver = Dish(4, "receiver", "接收者", null, 1, 1);
            TestBoard test = BuildBoard(2, 2, gummyDish, rowMate, source, receiver);

            ScoreResult result = Calculate(test, transfer, gummy,
                (candidates, count) => new[] { receiver.Id });

            ScoreLine applied = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.SweetTransferBuffApplied);
            Assert.That(applied.Trace.VisualTargetDishInstanceIds, Is.EquivalentTo(new[] { gummyDish.Id, source.Id }));
            ScoreLine triggered = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered);
            Assert.That(triggered.Trace.RuntimeSelfDishInstanceId, Is.EqualTo(source.Id));
            Assert.That(triggered.Trace.OwnerDishInstanceId, Is.EqualTo(gummyDish.Id));
            Assert.That(triggered.Trace.VisualTargetDishInstanceIds, Is.EquivalentTo(new[] { gummyDish.Id, rowMate.Id }));
            Assert.That(ScoreOf(result, gummyDish).Multiplier, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(ScoreOf(result, rowMate).Multiplier, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(ScoreOf(result, source).Multiplier, Is.EqualTo(1f));
        }

        [Test]
        public void MarshmallowBuff_AddsTwoTargetsAndCapsAtCandidates()
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能", transferCount: 1);
            SkillDef marshmallow = MarshmallowSkill();
            DishInstance marshmallowDish = Dish(1, "marshmallow", "棉花糖", marshmallow.Id, 0, 0);
            DishInstance source = Dish(2, "source", "来源", transfer.Id, 0, 1);
            DishInstance targetA = Dish(3, "a", "目标A", null, 1, 1);
            DishInstance targetB = Dish(4, "b", "目标B", null, 0, 2);
            DishInstance targetC = Dish(5, "c", "目标C", null, 1, 2);
            TestBoard test = BuildBoard(2, 3, marshmallowDish, source, targetA, targetB, targetC);
            int requested = 0;

            ScoreResult result = Calculate(test, transfer, marshmallow, (candidates, count) =>
            {
                requested = Math.Max(requested, count);
                return candidates.Where(id => id != marshmallowDish.Id).Take(count).ToArray();
            });

            Assert.That(requested, Is.EqualTo(3));
            Assert.That(result.SkillTransfers.Count, Is.EqualTo(3));
            ScoreLine trigger = result.ScoreLines.Single(line =>
                line.Kind == ScoreLineKind.SweetTransferBuffTriggered
                && line.Trace.ActionType == SkillActionType.TriggerSweetTransfer);
            Assert.That(trigger.Value, Is.EqualTo(2f));
            Assert.That(trigger.Trace.RuntimeSelfDishInstanceId, Is.EqualTo(source.Id));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyOrInvalidSelection_RecordsFailureWithoutTriggeringBuff(bool invalidId)
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能");
            SkillDef gummy = GummySkill();
            DishInstance gummyDish = Dish(1, "gummy", "软糖", gummy.Id, 0, 0);
            DishInstance source = Dish(2, "source", "来源", transfer.Id, 0, 1);
            TestBoard test = BuildBoard(1, 2, gummyDish, source);

            ScoreResult result = Calculate(test, transfer, gummy,
                (candidates, count) => invalidId ? new[] { 999999 } : Array.Empty<int>());

            ScoreLine failure = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.SweetTransferFailed);
            Assert.That(failure.Trace.RuntimeSelfDishInstanceId, Is.EqualTo(source.Id));
            Assert.That(failure.Trace.VisualTargetDishInstanceIds, Is.Empty);
            Assert.That(failure.Trace.VisualTargetCells, Is.Empty);
            Assert.That(result.ScoreLines, Has.None.Matches<ScoreLine>(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered));
            Assert.That(result.SkillTransfers, Is.Empty);
            Assert.That(ScoreOf(result, gummyDish).Multiplier, Is.EqualTo(1f));
        }

        [Test]
        public void MapleReplay_UsesRealSourceNameAndCanFailIndependently()
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能");
            SkillDef maple = MapleSkill();
            DishInstance source = Dish(1, "source", "草莓糖", transfer.Id, 0, 0);
            DishInstance target = Dish(2, "target", "目标", null, 1, 0);
            DishInstance mapleDish = Dish(3, "maple", "枫糖", maple.Id, 0, 1);
            TestBoard test = BuildBoard(2, 2, source, target, mapleDish);
            int transferAttempt = 0;

            ScoreResult result = Calculate(test, transfer, maple, (candidates, count) =>
            {
                transferAttempt++;
                return transferAttempt == 1 ? new[] { target.Id } : Array.Empty<int>();
            });

            ScoreLine replaySource = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.TriggeredSweetTransferSource);
            ScoreLine failure = result.ScoreLines.Single(line => line.Kind == ScoreLineKind.SweetTransferFailed);
            Assert.That(replaySource.Source.Name, Does.Contain("草莓糖").And.Not.Contain("枫糖<甜蜜传递>"));
            Assert.That(failure.Trace.RuntimeSelfDishInstanceId, Is.EqualTo(source.Id));
            Assert.That(failure.Source.Name, Does.Contain("草莓糖").And.Not.Contain("枫糖<甜蜜传递>"));
            Assert.That(result.SkillTransfers.Count, Is.EqualTo(1));
        }

        [Test]
        public void SourceAndMapleReplay_TriggerPersistentGummyBuffTwice()
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能");
            SkillDef gummy = GummySkill();
            SkillDef maple = MapleSkill();
            DishInstance gummyDish = Dish(1, "gummy", "软糖", gummy.Id, 0, 0);
            DishInstance source = Dish(2, "source", "来源", transfer.Id, 0, 1);
            DishInstance target = Dish(3, "target", "目标", null, 1, 1);
            DishInstance mapleDish = Dish(4, "maple", "枫糖", maple.Id, 0, 2);
            TestBoard test = BuildBoard(2, 3, gummyDish, source, target, mapleDish);

            ScoreResult result = Calculate(test, new[] { transfer, gummy, maple },
                (candidates, count) => candidates.Contains(target.Id)
                    ? new[] { target.Id }
                    : candidates.Take(count).ToArray());

            Assert.That(result.ScoreLines.Count(line => line.Kind == ScoreLineKind.SweetTransferBuffTriggered), Is.EqualTo(2));
            Assert.That(result.SkillTransfers.Count, Is.EqualTo(2));
            Assert.That(ScoreOf(result, gummyDish).Multiplier, Is.EqualTo(2.25f).Within(0.001f));
        }

        [Test]
        public void MultipleMarshmallowBuffs_AddTogetherAndCapAtCandidateCount()
        {
            SkillDef transfer = TransferSkill("transfer", "来源技能", transferCount: 1);
            SkillDef marshmallow = MarshmallowSkill();
            DishInstance marshmallowA = Dish(1, "marsh_a", "棉花糖A", marshmallow.Id, 0, 0);
            DishInstance marshmallowB = Dish(2, "marsh_b", "棉花糖B", marshmallow.Id, 0, 1);
            DishInstance source = Dish(3, "source", "来源", transfer.Id, 0, 2);
            DishInstance targetA = Dish(4, "a", "目标A", null, 1, 2);
            DishInstance targetB = Dish(5, "b", "目标B", null, 0, 3);
            DishInstance targetC = Dish(6, "c", "目标C", null, 1, 3);
            TestBoard test = BuildBoard(2, 4, marshmallowA, marshmallowB, source, targetA, targetB, targetC);
            int requested = 0;

            ScoreResult result = Calculate(test, transfer, marshmallow, (candidates, count) =>
            {
                requested = count;
                return candidates.Take(count).ToArray();
            });

            Assert.That(requested, Is.EqualTo(5), "1 个原目标 + 两层各 2 个额外目标，应按 5 个候选封顶");
            Assert.That(result.SkillTransfers.Count, Is.EqualTo(5));
            Assert.That(result.ScoreLines.Count(line =>
                line.Kind == ScoreLineKind.SweetTransferBuffTriggered
                && line.Trace.ActionType == SkillActionType.TriggerSweetTransfer), Is.EqualTo(2));
        }

        private static ScoreResult Calculate(
            TestBoard test,
            SkillDef firstSkill,
            SkillDef secondSkill,
            Func<IReadOnlyList<int>, int, IReadOnlyList<int>> selector)
        {
            var db = new GameplayDatabase(
                test.Dishes.Select(dish => dish.Def),
                new[] { firstSkill, secondSkill },
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new ScoreCalculator().Calculate(test.Board, db, transferTargetSelector: selector);
        }

        private static ScoreResult Calculate(
            TestBoard test,
            IReadOnlyList<SkillDef> skills,
            Func<IReadOnlyList<int>, int, IReadOnlyList<int>> selector)
        {
            var db = new GameplayDatabase(
                test.Dishes.Select(dish => dish.Def),
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new ScoreCalculator().Calculate(test.Board, db, transferTargetSelector: selector);
        }

        private static SkillDef TransferSkill(
            string id,
            string name,
            int transferCount = 1,
            SkillScope transferScope = SkillScope.All)
        {
            SkillRuleDef payload = Rule($"{id}_payload", id, 0, SkillActionType.AddFlat, SkillScope.Self, value: 2f);
            SkillRuleDef transfer = Rule($"{id}_transfer", id, 1, SkillActionType.TransferSkills, transferScope, transferCount);
            return new SkillDef(id, name, string.Empty, Array.Empty<string>(), new[] { payload, transfer }, new[] { "分数 +2", "甜蜜传递" });
        }

        private static SkillDef GummySkill()
        {
            SkillRuleDef rule = Rule(
                "gummy_buff",
                "gummy_skill",
                0,
                SkillActionType.AddMult,
                SkillScope.ColumnAndSelf,
                value: 1.5f,
                parameters: new[] { "when:transfer;resultscope:RowAndSelf" });
            return new SkillDef("gummy_skill", "软糖", string.Empty, Array.Empty<string>(), new[] { rule }, new[] { "甜蜜 Buff" });
        }

        private static SkillDef MarshmallowSkill()
        {
            SkillRuleDef rule = Rule(
                "marshmallow_buff",
                "marshmallow_skill",
                0,
                SkillActionType.TriggerSweetTransfer,
                SkillScope.Down,
                value: 2f,
                parameters: new[] { "modifier:add-targets" });
            return new SkillDef("marshmallow_skill", "棉花糖", string.Empty, Array.Empty<string>(), new[] { rule }, new[] { "甜蜜 Buff" });
        }

        private static SkillDef MapleSkill()
        {
            SkillRuleDef rule = Rule(
                "maple_trigger",
                "maple_skill",
                0,
                SkillActionType.TriggerSweetTransfer,
                SkillScope.All,
                count: 3,
                parameters: new[] { "skilltype:TransferSkills" });
            return new SkillDef("maple_skill", "枫糖", string.Empty, Array.Empty<string>(), new[] { rule }, new[] { "额外结算甜蜜传递" });
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            int order,
            SkillActionType actionType,
            SkillScope actionScope,
            int count = 0,
            float value = 0f,
            IReadOnlyList<string> parameters = null)
        {
            return new SkillRuleDef(
                id,
                skillId,
                order,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Gate,
                string.Empty,
                actionType,
                actionScope,
                count,
                new[] { value },
                parameters ?? Array.Empty<string>());
        }

        private static DishInstance Dish(int id, string defId, string name, string skillId, int x, int y)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            string[] skills = string.IsNullOrEmpty(skillId) ? Array.Empty<string>() : new[] { skillId };
            var def = new DishDef(defId, name, 10, shape, 0, 0, 1f, skills, string.Empty, allowRotate: false);
            return new DishInstance(id, def, new Placement(shape, 0, new GridPos(x, y)), skills, Array.Empty<string>());
        }

        private static TestBoard BuildBoard(int width, int height, params DishInstance[] dishes)
        {
            var board = new DiningTable(width, height);
            foreach (DishInstance dish in dishes)
            {
                board.Place(dish);
            }

            return new TestBoard(board, dishes);
        }

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);

        private sealed class TestBoard
        {
            public TestBoard(DiningTable board, IReadOnlyList<DishInstance> dishes)
            {
                Board = board;
                Dishes = dishes;
            }

            public DiningTable Board { get; }

            public IReadOnlyList<DishInstance> Dishes { get; }
        }
    }
}
