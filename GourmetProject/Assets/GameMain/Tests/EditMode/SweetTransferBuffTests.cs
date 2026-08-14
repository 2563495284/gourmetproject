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
        public void SoybeanGlutinousRoll_UsesLinearMultiplierFromEffectiveServings()
        {
            SkillRuleDef rollRule = Rule(
                "sk_soybean_glutinous_roll_1", "sk_soybean_glutinous_roll",
                SkillConditionType.DishCount, SkillScope.RoundAndSelf,
                CountUnit.Instances, CountMode.Per,
                SkillActionType.AddMult, SkillScope.RoundAndSelf,
                0.15f, "linear");
            SkillDef rollSkill = Skill("sk_soybean_glutinous_roll", rollRule);
            DishInstance roll = Dish(1, "soybean_glutinous_roll", 0, 0, 0, new[] { rollSkill.Id });
            DishInstance neighbor = Dish(2, "neighbor", 0, 1, 0, Array.Empty<string>(), countAs: 2);
            var table = new DiningTable(2, 1);
            table.Place(roll);
            table.Place(neighbor);

            ScoreResult result = new ScoreCalculator().Calculate(
                table, Database(new[] { roll.Def, neighbor.Def }, rollSkill));

            Assert.That(ScoreOf(result, roll).Multiplier.ToDouble(), Is.EqualTo(1.45d).Within(0.0001d));
            Assert.That(ScoreOf(result, neighbor).Multiplier.ToDouble(), Is.EqualTo(1.45d).Within(0.0001d));
        }

        [Test]
        public void PoppingCandy_ReceivedTargetsGrantEveryRegisteredDishPerTarget()
        {
            SkillRuleDef buffRule = Rule(
                "sk_popping_candy_1", "sk_popping_candy",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.AddFlat, SkillScope.ColumnAndSelf,
                40f, "when:receive-transfer;resultscope:BuffTargets");
            SkillDef poppingSkill = Skill("sk_popping_candy", buffRule);
            SkillDef transferSkill = TransferSkill("transfer_two_targets", 1, 2);
            DishInstance owner = Dish(1, "popping_candy", 0, 0, 0, new[] { poppingSkill.Id });
            DishInstance holderA = Dish(2, "holder_a", 0, 0, 1, Array.Empty<string>());
            DishInstance holderB = Dish(3, "holder_b", 0, 0, 2, Array.Empty<string>());
            DishInstance source = Dish(4, "source", 0, 1, 3, new[] { transferSkill.Id });
            var table = new DiningTable(2, 4);
            table.Place(owner);
            table.Place(holderA);
            table.Place(holderB);
            table.Place(source);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { owner.Def, holderA.Def, holderB.Def, source.Def }, poppingSkill, transferSkill),
                transferTargetSelector: (_, _) => new[] { holderA.Id, holderB.Id });

            Assert.That(ScoreOf(result, owner).FlatBonus.ToDouble(), Is.EqualTo(80d).Within(0.0001d));
            Assert.That(ScoreOf(result, holderA).FlatBonus.ToDouble(), Is.EqualTo(80d).Within(0.0001d));
            Assert.That(ScoreOf(result, holderB).FlatBonus.ToDouble(), Is.EqualTo(80d).Within(0.0001d));
            Assert.That(ScoreOf(result, source).FlatBonus.ToDouble(), Is.EqualTo(0d).Within(0.0001d));
        }

        [Test]
        public void Gummy_SuccessfulTransferGrantsEveryRegisteredDishOncePerExecution()
        {
            SkillRuleDef buffRule = Rule(
                "sk_gummy_1", "sk_gummy",
                SkillConditionType.None, SkillScope.ColumnAndSelf,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.AddMultFlat, SkillScope.ColumnAndSelf,
                0.7f, "when:transfer;resultscope:BuffTargets");
            SkillDef gummySkill = Skill("sk_gummy", buffRule);
            SkillDef transferSkill = TransferSkill("transfer_twice", 2, 2);
            DishInstance owner = Dish(1, "gummy", 0, 0, 0, new[] { gummySkill.Id });
            DishInstance source = Dish(2, "source", 0, 0, 1, new[] { transferSkill.Id });
            DishInstance targetA = Dish(3, "target_a", 0, 1, 2, Array.Empty<string>());
            DishInstance targetB = Dish(4, "target_b", 0, 2, 2, Array.Empty<string>());
            var table = new DiningTable(3, 3);
            table.Place(owner);
            table.Place(source);
            table.Place(targetA);
            table.Place(targetB);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { owner.Def, source.Def, targetA.Def, targetB.Def }, gummySkill, transferSkill),
                transferTargetSelector: (_, _) => new[] { targetA.Id, targetB.Id });

            Assert.That(ScoreOf(result, owner).Multiplier.ToDouble(), Is.EqualTo(2.4d).Within(0.0001d));
            Assert.That(ScoreOf(result, source).Multiplier.ToDouble(), Is.EqualTo(2.4d).Within(0.0001d));
            Assert.That(ScoreOf(result, targetA).Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
            Assert.That(ScoreOf(result, targetB).Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
        }

        [Test]
        public void BigLollipop_RowColumnUnionExecutesCrossingSourceOnlyOnce()
        {
            SkillDef transferSkill = TransferSkill("crossing_transfer", 1, 1);
            SkillRuleDef triggerRule = Rule(
                "sk_big_lollipop_1", "sk_big_lollipop",
                SkillConditionType.None, SkillScope.All,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.TriggerSweetTransfer, SkillScope.All,
                0f, "axis:rowcol;skilltype:TransferSkills");
            SkillDef lollipopSkill = Skill("sk_big_lollipop", triggerRule);
            DishInstance source = Dish(
                1, "crossing_source", 0, 0, 0, new[] { transferSkill.Id },
                shapeRows: new[] { ".X", "X." });
            DishInstance lollipop = Dish(2, "big_lollipop", 0, 1, 1, new[] { lollipopSkill.Id });
            DishInstance target = Dish(3, "target", 0, 2, 2, Array.Empty<string>());
            var table = new DiningTable(3, 3);
            table.Place(source);
            table.Place(lollipop);
            table.Place(target);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { source.Def, lollipop.Def, target.Def }, transferSkill, lollipopSkill),
                transferTargetSelector: (ids, count) => ids.Take(count).ToArray());

            Assert.That(
                result.ScoreLines.Count(line => line.Kind == ScoreLineKind.TriggeredSweetTransferSource),
                Is.EqualTo(1));
            Assert.That(ScoreOf(result, lollipop).Multiplier.ToDouble(), Is.EqualTo(1d).Within(0.0001d));
        }

        private static SkillRuleDef Rule(
            string id,
            string skillId,
            SkillConditionType conditionType,
            SkillScope conditionScope,
            CountUnit countUnit,
            CountMode countMode,
            SkillActionType actionType,
            SkillScope actionScope,
            float value,
            string actionParam,
            int order = 0,
            int actionCount = 0)
            => new SkillRuleDef(
                id, skillId, order, SkillTrigger.OnSettle,
                conditionType, conditionScope, countUnit, countMode, string.Empty,
                actionType, actionScope, actionCount, new[] { value },
                string.IsNullOrEmpty(actionParam) ? Array.Empty<string>() : new[] { actionParam });

        private static SkillDef Skill(string id, params SkillRuleDef[] rules)
            => new SkillDef(id, id, string.Empty, Array.Empty<string>(), rules);

        private static SkillDef TransferSkill(string id, int transferRuleCount, int targetCount)
        {
            var rules = new List<SkillRuleDef>
            {
                Rule(
                    id + "_payload", id,
                    SkillConditionType.None, SkillScope.Self,
                    CountUnit.Instances, CountMode.Gate,
                    SkillActionType.AddFlat, SkillScope.Self,
                    0f, string.Empty),
            };
            for (int i = 0; i < transferRuleCount; i++)
            {
                rules.Add(Rule(
                    id + "_transfer_" + (i + 1), id,
                    SkillConditionType.None, SkillScope.Self,
                    CountUnit.Instances, CountMode.Gate,
                    SkillActionType.TransferSkills, SkillScope.All,
                    0f, string.Empty, order: i + 1, actionCount: targetCount));
            }

            return Skill(id, rules.ToArray());
        }

        private static DishInstance Dish(
            int instanceId,
            string id,
            int deliciousness,
            int x,
            int y,
            IReadOnlyList<string> skillIds,
            int countAs = 1,
            IReadOnlyList<string> shapeRows = null)
        {
            DishShape shape = DishShape.FromRows(shapeRows ?? new[] { "X" });
            var def = new DishDef(
                id, id, deliciousness, shape, 0, 0, 1f,
                skillIds, string.Empty, countAs: countAs);
            return new DishInstance(
                instanceId,
                def,
                new Placement(shape, 0, new GridPos(x, y)),
                skillIds,
                Array.Empty<string>());
        }

        private static GameplayDatabase Database(
            IReadOnlyList<DishDef> dishes,
            params SkillDef[] skills)
            => new GameplayDatabase(
                dishes,
                skills,
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);
    }
}
