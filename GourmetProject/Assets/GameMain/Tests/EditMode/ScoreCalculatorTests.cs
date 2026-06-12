using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Tests
{
    /// <summary>计分管线测试：加法/乘区/相邻/空位效果、贡献汇总、结算顺序、局级修正。</summary>
    public class ScoreCalculatorTests
    {
        private static GameplayDatabase Db(params TagDef[] tags)
        {
            return new GameplayDatabase(new List<DishDef>(), tags, new List<RecipeDef>());
        }

        [Test]
        public void AddFlat_IncreasesContribution()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Tag("fresh", TagEffectType.AddFlat, 5f));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
            Assert.AreEqual(15, result.Total);
        }

        [Test]
        public void AddMult_MultipliesContribution()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Tag("sweet", TagEffectType.AddMult, 1.5f));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "sweet" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
        }

        [Test]
        public void FlatThenMult_AppliesFlatBeforeMultiplier()
        {
            GameplayDatabase db = Db(
                GameplayTestFactory.Tag("fresh", TagEffectType.AddFlat, 5f),
                GameplayTestFactory.Tag("sweet", TagEffectType.AddMult, 1.5f));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh", "sweet" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // (10 + 5) * 1.5 = 22.5 -> 四舍五入(向上) = 23
            Assert.AreEqual(22.5f, result.RawSum, 0.001f);
            Assert.AreEqual(23, result.Total);
        }

        [Test]
        public void PerAdjacentDish_ScalesWithNeighborCount()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Tag("spicy", TagEffectType.PerAdjacentDish, 3f));
            var board = new GpBoard(4, 4);
            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, deliciousness: 4, allowRotate: false);

            DishInstance spicy = GameplayTestFactory.InstanceWithTags(1, single, 1, 1, new[] { "spicy" });
            board.Place(spicy);
            board.Place(GameplayTestFactory.Instance(2, single, 0, 1)); // left neighbor
            board.Place(GameplayTestFactory.Instance(3, single, 2, 1)); // right neighbor

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            DishScore spicyScore = result.DishScores.First(s => s.DishInstanceId == 1);
            // 4 + 3*2 = 10
            Assert.AreEqual(10f, spicyScore.Contribution, 0.001f);
        }

        [Test]
        public void PerEmptyCell_ScalesWithBoardEmptyCells()
        {
            GameplayDatabase db = Db(GameplayTestFactory.Tag("lonely", TagEffectType.PerEmptyCell, 2f));
            var board = new GpBoard(4, 4); // 16 cells
            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, deliciousness: 9, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, single, 0, 0, new[] { "lonely" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 1 occupied -> 15 empty. 9 + 2*15 = 39
            Assert.AreEqual(39f, result.RawSum, 0.001f);
        }

        [Test]
        public void FinalModifiers_ApplyAfterSum()
        {
            GameplayDatabase db = Db();
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new string[0]));

            ScoreResult result = new ScoreCalculator().Calculate(board, db, finalFlat: 50f, finalMultiplier: 2f);

            // (10 + 50) * 2 = 120
            Assert.AreEqual(120, result.Total);
        }
    }
}
