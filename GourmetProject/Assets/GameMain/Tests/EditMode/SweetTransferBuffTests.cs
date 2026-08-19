using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Game.Presentation.Battle;
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
        public void EggYolkPastry_AddsSubSkillCountToColumnIncludingItself()
        {
            SkillRuleDef countAsRule = Rule(
                "sk_egg_yolk_pastry_1", "sk_egg_yolk_pastry",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.AddCountAs, SkillScope.ColumnAndSelf,
                1f, "source:target-skill-count");
            SkillDef eggSkill = Skill("sk_egg_yolk_pastry", countAsRule);
            SkillDef oneRuleSkill = NoOpSkill("one_rule_skill", 1);
            SkillDef twoRuleSkill = NoOpSkill("two_rule_skill", 2);
            SkillDef threeRuleSkill = NoOpSkill("three_rule_skill", 3);
            DishInstance egg = Dish(1, "egg_yolk_pastry", 0, 0, 0, new[] { eggSkill.Id }, countAs: 4);
            DishInstance oneRuleTarget = Dish(2, "one_rule_target", 0, 0, 1, new[] { oneRuleSkill.Id });
            DishInstance twoRuleTarget = Dish(3, "two_rule_target", 0, 0, 2, new[] { twoRuleSkill.Id });
            DishInstance threeRuleTarget = Dish(4, "three_rule_target", 0, 0, 3, new[] { threeRuleSkill.Id });
            var table = new DiningTable(1, 4);
            table.Place(egg);
            table.Place(oneRuleTarget);
            table.Place(twoRuleTarget);
            table.Place(threeRuleTarget);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(
                    new[] { egg.Def, oneRuleTarget.Def, twoRuleTarget.Def, threeRuleTarget.Def },
                    eggSkill,
                    oneRuleSkill,
                    twoRuleSkill,
                    threeRuleSkill));

            Assert.That(ScoreOf(result, egg).EffectiveCountAs, Is.EqualTo(5));
            Assert.That(ScoreOf(result, oneRuleTarget).EffectiveCountAs, Is.EqualTo(2));
            Assert.That(ScoreOf(result, twoRuleTarget).EffectiveCountAs, Is.EqualTo(3));
            Assert.That(ScoreOf(result, threeRuleTarget).EffectiveCountAs, Is.EqualTo(4));
            List<ScoreLine> countAsLines = result.ScoreLines
                .Where(line => line.Kind == ScoreLineKind.CountAs)
                .ToList();
            Assert.That(
                countAsLines.Select(line => line.Value.ToDouble()),
                Is.EquivalentTo(new[] { 1d, 1d, 2d, 3d }));

            // 一次蛋黄酥主动技能产生一个执行组；来源始终是蛋黄酥，所有目标结果同批展示。
            Assert.That(countAsLines.Select(line => line.ExecutionGroupId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(countAsLines[0].ExecutionGroupId, Is.GreaterThan(0));
            Assert.That(countAsLines, Has.All.Matches<ScoreLine>(line =>
                line.Source.Type == ScoreSourceType.DishSkill
                && line.Source.Id == eggSkill.Id
                && line.Source.DishInstanceId == egg.Id
                && line.Trace.OwnerDishInstanceId == egg.Id
                && line.Trace.RuntimeSelfDishInstanceId == egg.Id));

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(result);
            SettlementEffectGroup group = plan.Groups.Single(entry =>
                entry.Lines.Any(line => line.Kind == ScoreLineKind.CountAs));
            Assert.That(group.ActorDishInstanceId, Is.EqualTo(egg.Id));
            Assert.That(
                group.TargetDishIds,
                Is.EquivalentTo(new[] { egg.Id, oneRuleTarget.Id, twoRuleTarget.Id, threeRuleTarget.Id }));
            Assert.That(
                group.Trace.VisualTargetDishInstanceIds,
                Is.EquivalentTo(new[] { egg.Id, oneRuleTarget.Id, twoRuleTarget.Id, threeRuleTarget.Id }));
            List<List<int>> batches = SettlementSequencer.BuildResultLineBatches(group);
            Assert.That(batches.Count, Is.EqualTo(1));
            Assert.That(batches[0], Is.EquivalentTo(Enumerable.Range(0, countAsLines.Count)));
            Assert.That(
                SettlementStageView.FeedbackFor(countAsLines.Single(line => line.DishInstanceId == egg.Id)),
                Is.EqualTo(SettlementDishFeedbackKind.GenericValueChanged),
                "蛋黄酥只在效果组开场主动发动一次，自身结果行不得再次表现为发动技能");
        }

        [Test]
        public void SkillCount_UsesSubSkillEntriesInsteadOfSkillContainers()
        {
            SkillRuleDef scoreRule = Rule(
                "two_rules_1", "two_rules",
                SkillConditionType.SkillCount, SkillScope.Self,
                CountUnit.Instances, CountMode.Per,
                SkillActionType.AddFlat, SkillScope.Self,
                10f, string.Empty);
            SkillRuleDef noOpRule = Rule(
                "two_rules_2", "two_rules",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.None, SkillScope.Self,
                0f, string.Empty,
                order: 1);
            SkillDef skill = Skill("two_rules", scoreRule, noOpRule);
            DishInstance dish = Dish(1, "dish", 0, 0, 0, new[] { skill.Id });
            var table = new DiningTable(1, 1);
            table.Place(dish);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { dish.Def }, skill));

            Assert.That(ScoreOf(result, dish).FlatBonus.ToDouble(), Is.EqualTo(20d).Within(0.0001d));
        }

        [Test]
        public void RedVelvetCake_RandomLeftTargetBecomesCakeAndConsumesLayersWithPresentation()
        {
            SkillRuleDef categoryRule = Rule(
                "sk_red_velvet_cake_1", "sk_red_velvet_cake",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.AddTemporaryCategory, SkillScope.Left,
                1f, "cat:cake;target:random",
                actionCount: 1);
            SkillRuleDef layerRule = Rule(
                "sk_red_velvet_cake_2", "sk_red_velvet_cake",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.ConsumeLayer, SkillScope.CakeBuff,
                10f, string.Empty,
                order: 1);
            SkillDef skill = Skill("sk_red_velvet_cake", categoryRule, layerRule);
            DishInstance target = Dish(1, "target", 0, 0, 0, Array.Empty<string>());
            DishInstance cake = Dish(2, "red_velvet_cake", 0, 1, 0, new[] { skill.Id });
            var table = new DiningTable(2, 1);
            table.Place(target);
            table.Place(cake);

            ScoreResult result = new ScoreCalculator().Calculate(
                table,
                Database(new[] { target.Def, cake.Def }, skill),
                initialHappyCakeLayers: 15,
                randomIntegerSelector: (_, _) => 0);

            Assert.That(result.TemporaryCategories.Count, Is.EqualTo(1));
            Assert.That(result.TemporaryCategories[0].DishInstanceId, Is.EqualTo(target.Id));
            Assert.That(result.TemporaryCategories[0].Category, Is.EqualTo("cake"));
            Assert.That(result.HappyCakeLayerDelta, Is.EqualTo(-10));
            Assert.That(result.ScoreLines.Any(line =>
                line.Kind == ScoreLineKind.TemporaryCategory && line.DishInstanceId == target.Id), Is.True);
            Assert.That(result.ScoreLines.Any(line => line.Kind == ScoreLineKind.Layer), Is.True);
        }

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

        [TestCase(0, 8)]
        [TestCase(9999, 4)]
        public void ChocolateTruffle_RollsOncePerTargetAfterFixedBonuses(
            int randomRoll,
            int expectedTargetCount)
        {
            SkillRuleDef truffleRule = Rule(
                "sk_chocolate_truffle_1", "sk_chocolate_truffle",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.TriggerSweetTransfer, SkillScope.All,
                1f, "modifier:add-targets;chance:0.5");
            SkillRuleDef fixedBonusRule = Rule(
                "sk_marshmallow_1", "sk_marshmallow",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.TriggerSweetTransfer, SkillScope.All,
                2f, "modifier:add-targets");
            SkillDef truffleSkill = Skill("sk_chocolate_truffle", truffleRule);
            SkillDef fixedBonusSkill = Skill("sk_marshmallow", fixedBonusRule);
            SkillDef transferSkill = TransferSkill("transfer_two", 1, 2);
            DishInstance truffle = Dish(1, "chocolate_truffle", 0, 0, 0, new[] { truffleSkill.Id });
            DishInstance marshmallow = Dish(2, "marshmallow", 0, 1, 0, new[] { fixedBonusSkill.Id });
            DishInstance source = Dish(3, "source", 0, 2, 0, new[] { transferSkill.Id });
            var dishes = new List<DishInstance> { truffle, marshmallow, source };
            for (int i = 0; i < 8; i++)
            {
                dishes.Add(Dish(10 + i, "target_" + i, 0, i, 1, Array.Empty<string>()));
            }

            var table = new DiningTable(8, 2);
            foreach (DishInstance dish in dishes)
            {
                table.Place(dish);
            }

            int rollCount = 0;
            int selectedCount = 0;
            new ScoreCalculator().Calculate(
                table,
                Database(dishes.Select(dish => dish.Def).ToArray(), truffleSkill, fixedBonusSkill, transferSkill),
                transferTargetSelector: (ids, count) =>
                {
                    selectedCount = count;
                    return ids.Take(count).ToArray();
                },
                randomIntegerSelector: (_, _) =>
                {
                    rollCount++;
                    return randomRoll;
                });

            Assert.That(rollCount, Is.EqualTo(4), "原目标 2 + 固定额外目标 2，应独立判定 4 次");
            Assert.That(selectedCount, Is.EqualTo(expectedTargetCount));
        }

        [Test]
        public void ChocolateTruffle_MultipleOwnersRollIndependentlyAndTargetsRemainCapped()
        {
            SkillRuleDef truffleRule = Rule(
                "sk_chocolate_truffle_1", "sk_chocolate_truffle",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.TriggerSweetTransfer, SkillScope.All,
                1f, "modifier:add-targets;chance:0.5");
            SkillDef truffleSkill = Skill("sk_chocolate_truffle", truffleRule);
            SkillDef transferSkill = TransferSkill("transfer_one", 1, 1);
            DishInstance truffleA = Dish(1, "truffle_a", 0, 0, 0, new[] { truffleSkill.Id });
            DishInstance truffleB = Dish(2, "truffle_b", 0, 1, 0, new[] { truffleSkill.Id });
            DishInstance source = Dish(3, "source", 0, 2, 0, new[] { transferSkill.Id });
            var table = new DiningTable(3, 1);
            table.Place(truffleA);
            table.Place(truffleB);
            table.Place(source);

            int rollCount = 0;
            int selectedCount = 0;
            new ScoreCalculator().Calculate(
                table,
                Database(new[] { truffleA.Def, truffleB.Def, source.Def }, truffleSkill, transferSkill),
                transferTargetSelector: (ids, count) =>
                {
                    selectedCount = count;
                    return ids.Take(count).ToArray();
                },
                randomIntegerSelector: (_, _) =>
                {
                    rollCount++;
                    return 0;
                });

            Assert.That(rollCount, Is.EqualTo(2), "两个松露应各自对原目标独立判定一次");
            Assert.That(selectedCount, Is.EqualTo(2), "请求 3 个目标时应受两个合法候选封顶");
        }

        [Test]
        public void ChocolateTruffle_DoesNotAffectTransfersResolvedBeforeIt()
        {
            SkillDef transferSkill = TransferSkill("early_transfer", 1, 1);
            SkillRuleDef truffleRule = Rule(
                "sk_chocolate_truffle_1", "sk_chocolate_truffle",
                SkillConditionType.None, SkillScope.Self,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.TriggerSweetTransfer, SkillScope.All,
                1f, "modifier:add-targets;chance:0.5");
            SkillDef truffleSkill = Skill("sk_chocolate_truffle", truffleRule);
            DishInstance source = Dish(1, "source", 0, 0, 0, new[] { transferSkill.Id });
            DishInstance truffle = Dish(2, "chocolate_truffle", 0, 1, 0, new[] { truffleSkill.Id });
            DishInstance target = Dish(3, "target", 0, 2, 0, Array.Empty<string>());
            var table = new DiningTable(3, 1);
            table.Place(source);
            table.Place(truffle);
            table.Place(target);

            int rollCount = 0;
            int selectedCount = 0;
            new ScoreCalculator().Calculate(
                table,
                Database(new[] { source.Def, truffle.Def, target.Def }, transferSkill, truffleSkill),
                transferTargetSelector: (ids, count) =>
                {
                    selectedCount = count;
                    return ids.Take(count).ToArray();
                },
                randomIntegerSelector: (_, _) =>
                {
                    rollCount++;
                    return 0;
                });

            Assert.That(rollCount, Is.Zero);
            Assert.That(selectedCount, Is.EqualTo(1));
        }

        [Test]
        public void BigLollipop_RowColumnUnionExecutesCrossingSourceOnlyOnce()
        {
            SkillDef transferSkill = TransferSkill("crossing_transfer", 1, 1);
            SkillRuleDef triggerRule = Rule(
                "sk_big_lollipop_1", "sk_big_lollipop",
                SkillConditionType.None, SkillScope.All,
                CountUnit.Instances, CountMode.Gate,
                SkillActionType.TriggerSweetTransfer, SkillScope.RowAndColumn,
                0f, "skilltype:TransferSkills");
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

        private static SkillDef NoOpSkill(string id, int ruleCount)
            => Skill(
                id,
                Enumerable.Range(0, ruleCount)
                    .Select(index => Rule(
                        $"{id}_{index + 1}", id,
                        SkillConditionType.None, SkillScope.Self,
                        CountUnit.Instances, CountMode.Gate,
                        SkillActionType.None, SkillScope.Self,
                        0f, string.Empty,
                        order: index))
                    .ToArray());

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
                Array.Empty<RecipeDef>());

        private static DishScore ScoreOf(ScoreResult result, DishInstance dish)
            => result.DishScores.Single(score => score.DishInstanceId == dish.Id);
    }
}
