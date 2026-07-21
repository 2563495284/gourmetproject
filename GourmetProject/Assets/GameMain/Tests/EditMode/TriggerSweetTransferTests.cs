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
            foreach (DishInstance source in new[] { offAxis, column, row })
            {
                Assert.That(result.ScoreLines.Count(x =>
                    x.DishInstanceId == source.Id
                    && x.Kind == ScoreLineKind.DishFlat
                    && x.Source.Name == transferSkill.Name), Is.EqualTo(1), "大棒棒糖不能重跑来源技能的其他子技能");
            }
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
