using System;
using System.Linq;
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

        private static DishInstance Dish(int id, string dishId, DishShape shape, int x)
        {
            var def = new DishDef(
                dishId,
                dishId,
                deliciousness: 10,
                shape: shape,
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: Array.Empty<string>(),
                flavorId: string.Empty);
            var placement = new Placement(shape, rotationIndex: 0, origin: new GridPos(x, 0));
            return new DishInstance(
                id,
                def,
                placement,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
