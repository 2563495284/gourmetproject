using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class ExtraSweetTransferTests
    {
        [Test]
        public void NativeExtraSweetTransfer_WritesMarkerThenRerollsWhenTargetTransfers()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef extraSkill = Skill(
                "skill_extra",
                Rule("extra", "skill_extra", 0, SkillActionType.ExtraSweetTransfer, SkillScope.All, 1f, 1));
            SkillDef transferSkill = TransferSkill("skill_transfer", SkillScope.Other);
            DishDef extraDef = Dish("extra_dish", cell, "skill_extra");
            DishDef sourceDef = Dish("source_dish", cell, "skill_transfer");
            DishDef targetADef = Dish("target_a", cell);
            DishDef targetBDef = Dish("target_b", cell);
            GameplayDatabase db = Database(new[] { extraDef, sourceDef, targetADef, targetBDef }, extraSkill, transferSkill);
            var table = new DiningTable(4, 1);
            DishInstance extra = Instance(1, extraDef, cell, 0);
            DishInstance source = Instance(2, sourceDef, cell, 1);
            DishInstance targetA = Instance(3, targetADef, cell, 2);
            DishInstance targetB = Instance(4, targetBDef, cell, 3);
            Place(table, extra, source, targetA, targetB);

            int roll = 0;
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) =>
                {
                    int selected = roll++ == 0 ? targetA.Id : targetB.Id;
                    Assert.That(candidates, Does.Contain(selected));
                    return new[] { selected };
                });

            Assert.That(roll, Is.EqualTo(2), "写入额外次数时不应立即触发，原生甜蜜传递随后应执行 1+1 次");
            Assert.That(result.SkillTransfers.Select(x => x.TargetInstanceId), Is.EqualTo(new[] { targetA.Id, targetB.Id }));
        }

        [Test]
        public void CopiedExtraSweetTransfer_DoesNotWriteMarker()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef extraSkill = Skill(
                "skill_extra",
                Rule("extra", "skill_extra", 0, SkillActionType.ExtraSweetTransfer, SkillScope.All, 1f, 1));
            SkillDef transferSkill = TransferSkill("skill_transfer", SkillScope.Other);
            DishDef copiedExtraDef = Dish("copied_extra", cell);
            DishDef sourceDef = Dish("source_dish", cell, "skill_transfer");
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(new[] { copiedExtraDef, sourceDef, targetDef }, extraSkill, transferSkill);
            var table = new DiningTable(3, 1);
            DishInstance copiedExtra = Instance(1, copiedExtraDef, cell, 0);
            copiedExtra.AddSkill(extraSkill.Id, "原食物<技能复制>");
            DishInstance source = Instance(2, sourceDef, cell, 1);
            DishInstance target = Instance(3, targetDef, cell, 2);
            Place(table, copiedExtra, source, target);

            int roll = 0;
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) =>
                {
                    roll++;
                    return new[] { target.Id };
                });

            Assert.That(roll, Is.EqualTo(1));
            Assert.That(result.SkillTransfers.Count, Is.EqualTo(1));
        }

        [Test]
        public void SweetTransfer_DoesNotCarryExtraSweetTransferSubSkill()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            DishShape twoCells = DishShape.FromRows(new[] { "XX" });
            SkillRuleDef conditionalExtra = new SkillRuleDef(
                "conditional_extra",
                "skill_carrier",
                0,
                SkillTrigger.OnSettle,
                SkillConditionType.OccupiedCell,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Reach,
                "gte:2",
                SkillActionType.ExtraSweetTransfer,
                SkillScope.All,
                1,
                new[] { 1f },
                Array.Empty<string>());
            SkillRuleDef carriedFlat = Rule("carried_flat", "skill_carrier", 1, SkillActionType.AddFlat, SkillScope.Self, 1f);
            SkillRuleDef carrierTransfer = Rule("carrier_transfer", "skill_carrier", 2, SkillActionType.TransferSkills, SkillScope.Other, 0f, 1);
            SkillDef carrierSkill = Skill("skill_carrier", conditionalExtra, carriedFlat, carrierTransfer);
            SkillDef sourceSkill = TransferSkill("skill_source", SkillScope.Other);
            DishDef carrierDef = Dish("carrier", cell, "skill_carrier");
            DishDef receiverDef = Dish("receiver", twoCells);
            DishDef sourceDef = Dish("source", cell, "skill_source");
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(new[] { carrierDef, receiverDef, sourceDef, targetDef }, carrierSkill, sourceSkill);
            var table = new DiningTable(5, 1);
            DishInstance carrier = Instance(1, carrierDef, cell, 0);
            DishInstance receiver = Instance(2, receiverDef, twoCells, 1);
            DishInstance source = Instance(3, sourceDef, cell, 3);
            DishInstance target = Instance(4, targetDef, cell, 4);
            Place(table, carrier, receiver, source, target);

            int roll = 0;
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) =>
                {
                    int selected = roll++ == 0 ? receiver.Id : target.Id;
                    Assert.That(candidates, Does.Contain(selected));
                    return new[] { selected };
                });

            Assert.That(roll, Is.EqualTo(2));
            SkillTransferSideEffect carrierTransferResult = result.SkillTransfers.First(x => x.SourceInstanceId == carrier.Id);
            Assert.That(carrierTransferResult.Effects.Any(x => x.Rule.ActionType == SkillActionType.ExtraSweetTransfer), Is.False);
        }

        [Test]
        public void TriggerSweetTransfer_CopiedTransferDoesNotConsumeExtraMarker()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef extraSkill = Skill(
                "skill_extra",
                Rule("extra", "skill_extra", 0, SkillActionType.ExtraSweetTransfer, SkillScope.All, 1f, 1));
            SkillDef triggerSkill = Skill(
                "skill_trigger",
                Rule("trigger", "skill_trigger", 0, SkillActionType.TriggerSweetTransfer, SkillScope.All, 0f));
            SkillDef nativeTransfer = TransferSkill("skill_native_transfer", SkillScope.Other);
            SkillDef copiedTransfer = TransferSkill("skill_copied_transfer", SkillScope.Other);
            DishDef extraDef = Dish("extra_dish", cell, "skill_extra");
            DishDef triggerDef = Dish("trigger_dish", cell, "skill_trigger");
            DishDef sourceDef = Dish("source_dish", cell, "skill_native_transfer");
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(
                new[] { extraDef, triggerDef, sourceDef, targetDef },
                extraSkill,
                triggerSkill,
                nativeTransfer,
                copiedTransfer);
            var table = new DiningTable(4, 1);
            DishInstance extra = Instance(1, extraDef, cell, 0);
            DishInstance trigger = Instance(2, triggerDef, cell, 1);
            DishInstance source = Instance(3, sourceDef, cell, 2);
            source.AddSkill(copiedTransfer.Id, "原食物<技能复制>");
            DishInstance target = Instance(4, targetDef, cell, 3);
            Place(table, extra, trigger, source, target);

            int roll = 0;
            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) =>
                {
                    roll++;
                    Assert.That(candidates, Does.Contain(target.Id));
                    return new[] { target.Id };
                });

            // 代触发：原生 2 次 + copy 1 次；B 自身结算：原生 2 次 + copy 1 次。
            Assert.That(roll, Is.EqualTo(6));
            Assert.That(result.SkillTransfers.Count(x => x.TargetInstanceId == target.Id), Is.EqualTo(6));
        }

        private static SkillDef TransferSkill(string skillId, SkillScope scope)
        {
            return Skill(
                skillId,
                Rule($"{skillId}_flat", skillId, 0, SkillActionType.AddFlat, SkillScope.Self, 1f),
                Rule($"{skillId}_transfer", skillId, 1, SkillActionType.TransferSkills, scope, 0f, 1));
        }

        private static SkillDef Skill(string id, params SkillRuleDef[] rules)
        {
            return new SkillDef(
                id,
                id,
                string.Empty,
                Array.Empty<string>(),
                rules,
                rules.Select(x => x.Id).ToArray());
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            int order,
            SkillActionType action,
            SkillScope scope,
            float value,
            int actionCount = 0)
        {
            return new SkillRuleDef(
                id,
                skillId,
                order,
                SkillTrigger.OnSettle,
                SkillConditionType.None,
                SkillScope.Self,
                CountUnit.Instances,
                CountMode.Per,
                string.Empty,
                action,
                scope,
                actionCount,
                new[] { value },
                Array.Empty<string>());
        }

        private static DishDef Dish(string id, DishShape shape, string skillId = null)
        {
            return new DishDef(
                id,
                id,
                10,
                shape,
                0,
                0,
                1f,
                string.IsNullOrEmpty(skillId) ? Array.Empty<string>() : new[] { skillId },
                string.Empty,
                false);
        }

        private static DishInstance Instance(int id, DishDef def, DishShape shape, int x)
        {
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, 0)),
                def.SkillIds,
                Array.Empty<string>());
        }

        private static GameplayDatabase Database(DishDef[] dishes, params SkillDef[] skills)
        {
            return new GameplayDatabase(
                dishes,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
        }

        private static void Place(DiningTable table, params DishInstance[] dishes)
        {
            foreach (DishInstance dish in dishes)
            {
                table.Place(dish);
            }
        }
    }
}
