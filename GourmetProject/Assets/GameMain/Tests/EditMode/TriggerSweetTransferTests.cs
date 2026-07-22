using System;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TriggerSweetTransferTests
    {
        [Test]
        public void MapleSugar_TriggersOneSweetTransferWithoutReplayingSourceSkill()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef transferSkill = TransferSkill("skill_source");
            SkillDef mapleSkill = Skill(
                "skill_maple",
                Rule("maple_trigger", "skill_maple", 0, SkillActionType.TriggerSweetTransfer, SkillScope.All, 1));
            DishDef sourceDef = Dish("source", cell, "skill_source");
            DishDef targetDef = Dish("target", cell);
            DishDef mapleDef = Dish("maple", cell, "skill_maple");
            GameplayDatabase db = Database(new[] { sourceDef, targetDef, mapleDef }, transferSkill, mapleSkill);
            var table = new DiningTable(3, 1);
            DishInstance source = Instance(1, sourceDef, cell, 0, 0);
            DishInstance target = Instance(2, targetDef, cell, 1, 0);
            DishInstance maple = Instance(3, mapleDef, cell, 2, 0);
            Place(table, source, target, maple);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) => new[] { target.Id });

            Assert.That(result.SkillTransfers.Count(x => x.SourceInstanceId == source.Id), Is.EqualTo(2));
            Assert.That(result.ScoreLines.Count(x =>
                x.DishInstanceId == source.Id
                && x.Kind == ScoreLineKind.DishFlat
                && x.Source.Name == transferSkill.Name), Is.EqualTo(1), "代触发不能重跑来源技能的加分子技能");
            Assert.That(result.ScoreLines.Count(x =>
                x.DishInstanceId == target.Id
                && x.Kind == ScoreLineKind.DishFlat
                && x.Source.Name == "source<甜蜜传递>"), Is.EqualTo(2));
        }

        [Test]
        public void BigLollipop_TriggersRowAndColumnSourcesButNotOffAxisSource()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef transferSkill = TransferSkill("skill_source");
            SkillDef bigLollipopSkill = Skill(
                "skill_big_lollipop",
                Rule(
                    "big_lollipop_trigger",
                    "skill_big_lollipop",
                    0,
                    SkillActionType.TriggerSweetTransfer,
                    SkillScope.All,
                    0,
                    "axis:rowcol;skilltype:TransferSkills"));
            DishDef sourceDef = Dish("source", cell, "skill_source");
            DishDef bigLollipopDef = Dish("big_lollipop", cell, "skill_big_lollipop");
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(new[] { sourceDef, bigLollipopDef, targetDef }, transferSkill, bigLollipopSkill);
            var table = new DiningTable(3, 3);
            DishInstance offAxis = Instance(1, sourceDef, cell, 0, 0);
            DishInstance column = Instance(2, sourceDef, cell, 1, 0);
            DishInstance row = Instance(3, sourceDef, cell, 0, 1);
            DishInstance bigLollipop = Instance(4, bigLollipopDef, cell, 1, 1);
            DishInstance target = Instance(5, targetDef, cell, 2, 2);
            Place(table, offAxis, column, row, bigLollipop, target);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) => new[] { target.Id });

            Assert.That(result.SkillTransfers.Count(x => x.SourceInstanceId == row.Id), Is.EqualTo(2));
            Assert.That(result.SkillTransfers.Count(x => x.SourceInstanceId == column.Id), Is.EqualTo(2));
            Assert.That(result.SkillTransfers.Count(x => x.SourceInstanceId == offAxis.Id), Is.EqualTo(1));
            ScoreLine triggerCue = result.ScoreLines.Single(x => x.Kind == ScoreLineKind.TriggerSweetTransfer);
            Assert.That(triggerCue.DishInstanceId, Is.EqualTo(bigLollipop.Id), "代触发表现应归属大棒棒糖");
            Assert.That(triggerCue.Value, Is.EqualTo(2f), "表现应记录实际被代触发的食物数");
            int[] triggeredSourceOrder = result.ScoreLines
                .Where(x => x.Kind == ScoreLineKind.TriggeredSweetTransferSource)
                .Select(x => x.DishInstanceId)
                .ToArray();
            Assert.That(
                triggeredSourceOrder,
                Is.EqualTo(new[] { column.Id, row.Id }),
                "被代触发食物应按实际占格从上到下、再从左到右执行");
            foreach (DishInstance source in new[] { offAxis, column, row })
            {
                Assert.That(result.ScoreLines.Count(x =>
                    x.DishInstanceId == source.Id
                    && x.Kind == ScoreLineKind.DishFlat
                    && x.Source.Name == transferSkill.Name), Is.EqualTo(1), "大棒棒糖不能重跑来源技能的其他子技能");
            }
        }

        [Test]
        public void BigLollipop_ResolvesTriggeredTransfersBackToItselfDuringItsSkillPhase()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef transferSkill = TransferSkill("skill_source");
            SkillDef bigLollipopSkill = Skill(
                "skill_big_lollipop",
                Rule(
                    "big_lollipop_trigger",
                    "skill_big_lollipop",
                    0,
                    SkillActionType.TriggerSweetTransfer,
                    SkillScope.All,
                    0,
                    "axis:rowcol;skilltype:TransferSkills"));
            DishDef sourceDef = Dish("source", cell, "skill_source");
            DishDef bigLollipopDef = Dish("big_lollipop", cell, "skill_big_lollipop");
            GameplayDatabase db = Database(new[] { sourceDef, bigLollipopDef }, transferSkill, bigLollipopSkill);
            var table = new DiningTable(3, 3);
            DishInstance bigLollipop = Instance(1, bigLollipopDef, cell, 1, 0);
            DishInstance upperSource = Instance(2, sourceDef, cell, 1, 1);
            DishInstance lowerSource = Instance(3, sourceDef, cell, 1, 2);
            Place(table, bigLollipop, upperSource, lowerSource);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) => new[] { bigLollipop.Id });

            Assert.That(result.ScoreLines.Count(x =>
                x.DishInstanceId == bigLollipop.Id
                && x.Kind == ScoreLineKind.DishFlat
                && x.Source.Name == "source<甜蜜传递>"), Is.EqualTo(4),
                "两个来源应在大棒棒糖代触发时立即各生效一次，之后自身结算再各生效一次");
        }

        [Test]
        public void SweetTransfer_ToUnsettledDish_ResolvesImmediately()
        {
            DishShape cell = DishShape.FromRows(new[] { "X" });
            SkillDef transferSkill = TransferSkill("skill_source");
            DishDef sourceDef = Dish("source", cell, "skill_source");
            DishDef targetDef = Dish("target", cell);
            GameplayDatabase db = Database(new[] { sourceDef, targetDef }, transferSkill);
            var table = new DiningTable(1, 2);
            DishInstance source = Instance(1, sourceDef, cell, 0, 0);
            DishInstance target = Instance(2, targetDef, cell, 0, 1);
            Place(table, source, target);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                db,
                transferTargetSelector: (candidates, count) => new[] { target.Id });

            int transferredEffectIndex = result.ScoreLines
                .Select((line, index) => new { line, index })
                .Single(x =>
                    x.line.DishInstanceId == target.Id
                    && x.line.Kind == ScoreLineKind.DishFlat
                    && x.line.Source.Name == "source<甜蜜传递>")
                .index;
            int targetBaseIndex = result.ScoreLines
                .Select((line, index) => new { line, index })
                .Single(x =>
                    x.line.DishInstanceId == target.Id
                    && x.line.Kind == ScoreLineKind.DishBase)
                .index;

            Assert.That(
                transferredEffectIndex,
                Is.LessThan(targetBaseIndex),
                "目标尚未开始自身结算时，收到的甜蜜传递子技能也必须当场执行");
        }

        private static SkillDef TransferSkill(string skillId)
        {
            return Skill(
                skillId,
                Rule($"{skillId}_flat", skillId, 0, SkillActionType.AddFlat, SkillScope.Self),
                Rule($"{skillId}_transfer", skillId, 1, SkillActionType.TransferSkills, SkillScope.Other, 1));
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
            SkillScope actionScope,
            int actionCount = 0,
            string actionParam = null)
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
                actionScope,
                actionCount,
                new[] { action == SkillActionType.AddFlat ? 5f : 0f },
                string.IsNullOrEmpty(actionParam) ? Array.Empty<string>() : new[] { actionParam });
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

        private static DishInstance Instance(int id, DishDef def, DishShape shape, int x, int y)
        {
            return new DishInstance(
                id,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
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
