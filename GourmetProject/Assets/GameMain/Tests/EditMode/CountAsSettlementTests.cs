using System;
using System.Linq;
using BreakInfinity;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CountAsSettlementTests
    {
        [Test]
        public void CountAsBonusAll_AppliesAtSettlementAndBuildsRelicPreludeForEveryDish()
        {
            var table = new DiningTable(2, 1);
            DishShape shape = DishShape.FromRows(new[] { "X" });
            DishInstance first = Dish(1, "dish_first", shape, 0);
            DishInstance second = Dish(2, "dish_second", shape, 1);
            table.Place(first);
            table.Place(second);

            var db = new GameplayDatabase(
                new[] { first.Def, second.Def },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var source = new ItemScoreEffectSource(new[]
            {
                new ItemScoreSpec(
                    ItemScoreEffectType.CountAsBonusAll,
                    value: 2f,
                    param: string.Empty,
                    itemId: "item_count_as_all",
                    itemName: "九格拼盘"),
            });

            Assert.AreEqual(1, first.EffectiveCountAs, "点击结算前不应提前增加份数。");
            Assert.AreEqual(1, second.EffectiveCountAs, "点击结算前不应提前增加份数。");

            ScoreResult result = new ScoreCalculator(effectSources: new[] { source })
                .Calculate(table, db);

            CollectionAssert.AreEqual(
                new[] { 3, 3 },
                result.DishScores.Select(score => score.EffectiveCountAs).ToArray());
            Assert.AreEqual(1, first.EffectiveCountAs, "结算加成只在本次结算 live 生效，不应写回食物实例。");
            Assert.AreEqual(1, second.EffectiveCountAs, "结算加成只在本次结算 live 生效，不应写回食物实例。");

            ScoreLine[] countAsLines = result.ScoreLines
                .Where(line => line.Kind == ScoreLineKind.CountAs)
                .ToArray();
            Assert.AreEqual(2, countAsLines.Length);
            Assert.IsTrue(countAsLines.All(line => line.Phase == ScorePhase.BeforeAll));
            Assert.IsTrue(countAsLines.All(line => line.Source.Type == ScoreSourceType.Relic));
            Assert.IsTrue(countAsLines.All(line => line.Source.Id == "item_count_as_all"));
            Assert.IsTrue(countAsLines.All(line => line.Before == 1));
            Assert.IsTrue(countAsLines.All(line => line.After == 3));

            SettlementPresentationPlan plan = SettlementPresentationPlan.Build(result);
            Assert.AreEqual(1, plan.PreludeGroups.Count, "装饰品应先形成一个结算开场效果组。");
            Assert.AreEqual("item_count_as_all", plan.PreludeGroups[0].Source.Id);
            Assert.AreEqual(2, plan.PreludeGroups[0].Lines.Count, "同一装饰品应在一组内对全场食物播放份数增加。");
        }

        [Test]
        public void DishSizeAtMostThree_BuffsOnlyMatchingDishes()
        {
            const string skillId = "skill_hawthorn";
            var rule = new SkillRuleDef(
                id: "skill_hawthorn_1",
                skillId: skillId,
                order: 0,
                trigger: SkillTrigger.OnSettle,
                condType: SkillConditionType.DishSize,
                condScope: SkillScope.All,
                condUnit: CountUnit.Instances,
                condMode: CountMode.Per,
                condParam: "<=3",
                actionType: SkillActionType.AddFlat,
                actionScope: SkillScope.All,
                actionCount: 0,
                actionValues: new[] { 5f },
                actionParams: new[] { "size:<=3" });
            var skill = new SkillDef(
                skillId,
                "山楂糕",
                string.Empty,
                Array.Empty<string>(),
                new[] { rule });

            var table = new DiningTable(10, 1);
            DishInstance sizeThree = Dish(1, "size_three", DishShape.FromRows(new[] { "XXX" }), 0, skillId);
            DishInstance sizeOne = Dish(2, "size_one", DishShape.FromRows(new[] { "X" }), 3);
            DishInstance sizeTwo = Dish(3, "size_two", DishShape.FromRows(new[] { "XX" }), 4);
            DishInstance sizeFour = Dish(4, "size_four", DishShape.FromRows(new[] { "XXXX" }), 6);
            table.Place(sizeThree);
            table.Place(sizeOne);
            table.Place(sizeTwo);
            table.Place(sizeFour);

            var db = new GameplayDatabase(
                new[] { sizeThree.Def, sizeOne.Def, sizeTwo.Def, sizeFour.Def },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            CollectionAssert.AreEqual(
                new[] { new BigDouble(15), new BigDouble(15), new BigDouble(15), BigDouble.Zero },
                result.DishScores.Select(score => score.FlatBonus).ToArray());
        }

        [Test]
        public void OccupiedCellCountDividedByTwo_AddsFlooredCountAsBonus()
        {
            const string skillId = "skill_mala_gao";
            var rule = new SkillRuleDef(
                id: "skill_mala_gao_1",
                skillId: skillId,
                order: 0,
                trigger: SkillTrigger.OnSettle,
                condType: SkillConditionType.None,
                condScope: SkillScope.Self,
                condUnit: CountUnit.Instances,
                condMode: CountMode.Gate,
                condParam: string.Empty,
                actionType: SkillActionType.AddCountAs,
                actionScope: SkillScope.All,
                actionCount: 0,
                actionValues: new[] { 1f },
                actionParams: new[] { "target:occupiedcells;div:2" });
            var skill = new SkillDef(
                skillId,
                "马拉糕",
                string.Empty,
                Array.Empty<string>(),
                new[] { rule });

            var table = new DiningTable(10, 1);
            DishInstance sizeOne = Dish(1, "size_one", DishShape.FromRows(new[] { "X" }), 0, skillId);
            DishInstance sizeTwo = Dish(2, "size_two", DishShape.FromRows(new[] { "XX" }), 1);
            DishInstance sizeThree = Dish(3, "size_three", DishShape.FromRows(new[] { "XXX" }), 3);
            DishInstance sizeFour = Dish(4, "size_four", DishShape.FromRows(new[] { "XXXX" }), 6);
            table.Place(sizeOne);
            table.Place(sizeTwo);
            table.Place(sizeThree);
            table.Place(sizeFour);

            var db = new GameplayDatabase(
                new[] { sizeOne.Def, sizeTwo.Def, sizeThree.Def, sizeFour.Def },
                new[] { skill },
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());

            ScoreResult result = new ScoreCalculator().Calculate(table, db);

            CollectionAssert.AreEqual(
                new[] { 1, 2, 2, 3 },
                result.DishScores.Select(score => score.EffectiveCountAs).ToArray());
        }

        private static DishInstance Dish(
            int id,
            string dishId,
            DishShape shape,
            int x,
            params string[] skillIds)
        {
            skillIds ??= Array.Empty<string>();
            var def = new DishDef(
                dishId,
                dishId,
                deliciousness: 10,
                shape: shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: skillIds,
                flavorId: string.Empty);
            var placement = new Placement(shape, rotationIndex: 0, origin: new GridPos(x, 0));
            return new DishInstance(
                id,
                def,
                placement,
                skillIds,
                Array.Empty<string>());
        }
    }
}
