using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Tests
{
    /// <summary>风味第一批：甜/苦结算优先级层级排序、锈结算金币。</summary>
    public class FlavorLayerTests
    {
        private static GameplayDatabase Db(params FlavorDef[] flavors)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                new List<SkillDef>(),
                flavors,
                new List<MaterialDef>(),
                new List<RecipeDef>());
        }

        private static FlavorDef Flavor(string id, FlavorEffectType type, float value)
        {
            return new FlavorDef(id, id, id, type, new[] { value }, System.Array.Empty<string>(), string.Empty);
        }

        private static DishInstance PlaceWithFlavors(GpTable board, int id, DishDef def, int x, int y, params string[] flavors)
        {
            var placement = new Placement(def.Shape.RotatedBy(0), 0, new GridPos(x, y));
            var inst = new DishInstance(id, def, placement, System.Array.Empty<string>(), flavors);
            board.Place(inst);
            return inst;
        }

        [Test]
        public void Sweet_RaisesLayer_SettlesBeforeLowerLayerDish()
        {
            GameplayDatabase db = Db(Flavor("sweet", FlavorEffectType.SettlementLayer, 1f));
            var board = new GpTable(4, 4);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);

            // 甜菜在右(1,0)，普通菜在左(0,0)：按位置本应普通先，但甜层级+1 应先结算。
            PlaceWithFlavors(board, 1, d, 0, 0); // plain, layer 0
            PlaceWithFlavors(board, 2, d, 1, 0, "sweet"); // layer +1

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(2, result.DishScores[0].DishInstanceId, "甜(层级+1)应先结算");
            Assert.AreEqual(1, result.DishScores[1].DishInstanceId);
        }

        [Test]
        public void Bitter_LowersLayer_SettlesLast()
        {
            GameplayDatabase db = Db(Flavor("bitter", FlavorEffectType.SettlementLayer, -1f));
            var board = new GpTable(4, 4);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);

            // 苦菜在左上(0,0)，普通菜在右(1,0)：按位置本应苦先，但苦层级-1 应最后结算。
            PlaceWithFlavors(board, 1, d, 0, 0, "bitter"); // layer -1
            PlaceWithFlavors(board, 2, d, 1, 0); // layer 0

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(2, result.DishScores[0].DishInstanceId);
            Assert.AreEqual(1, result.DishScores[1].DishInstanceId, "苦(层级-1)应最后结算");
        }

        [Test]
        public void Sweet_Stacks_HigherLayerSettlesFirst()
        {
            GameplayDatabase db = Db(Flavor("sweet", FlavorEffectType.SettlementLayer, 1f));
            var board = new GpTable(4, 4);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);

            // 甜×1 在左(0,0)=层级1；甜×2 在右(1,0)=层级2 → 层级高者先。
            PlaceWithFlavors(board, 1, d, 0, 0, "sweet");
            PlaceWithFlavors(board, 2, d, 1, 0, "sweet", "sweet");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(2, result.DishScores[0].DishInstanceId, "甜×2(层级2)应先于甜×1(层级1)");
        }

        [Test]
        public void SweetAndBitter_Cancel_FallBackToBoardOrder()
        {
            GameplayDatabase db = Db(
                Flavor("sweet", FlavorEffectType.SettlementLayer, 1f),
                Flavor("bitter", FlavorEffectType.SettlementLayer, -1f));
            var board = new GpTable(4, 4);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);

            PlaceWithFlavors(board, 1, d, 1, 0, "sweet", "bitter"); // layer 0
            PlaceWithFlavors(board, 2, d, 0, 0); // layer 0

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 层级都为 0 → 回退棋盘顺序：左上(0,0)=id2 先。
            Assert.AreEqual(2, result.DishScores[0].DishInstanceId);
        }

        [Test]
        public void Rust_GrantsGoldOnSettle()
        {
            GameplayDatabase db = Db(Flavor("rust", FlavorEffectType.GrantGold, 5f));
            var board = new GpTable(4, 4);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            PlaceWithFlavors(board, 1, d, 0, 0, "rust");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(5f, result.GoldDelta, 0.001f);
            Assert.AreEqual(10f, result.RawSum, 0.001f, "锈只给金币，不改分数");
        }
    }
}
