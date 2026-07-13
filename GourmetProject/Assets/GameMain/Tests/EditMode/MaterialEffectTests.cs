using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Tests
{
    /// <summary>餐桌材质第一批：7 种材质公式、按食物×材质聚合、多材质排序、银掷骰请求。</summary>
    public class MaterialEffectTests
    {
        private static GameplayDatabase Db(params MaterialDef[] materials)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                new List<SkillDef>(),
                new List<FlavorDef>(),
                materials,
                new List<RecipeDef>());
        }

        private static Dictionary<GridPos, IReadOnlyList<string>> Cells(params (int x, int y, string mat)[] entries)
        {
            var map = new Dictionary<GridPos, IReadOnlyList<string>>();
            foreach ((int x, int y, string mat) in entries)
            {
                map[new GridPos(x, y)] = new List<string> { mat };
            }

            return map;
        }

        private static DishInstance Place(GpTable board, int id, DishDef def, int x, int y)
        {
            var placement = new Placement(def.Shape.RotatedBy(0), 0, new GridPos(x, y));
            var inst = new DishInstance(id, def, placement, System.Array.Empty<string>(), System.Array.Empty<string>());
            board.Place(inst);
            return inst;
        }

        [Test]
        public void Cherry_AddsFlatScore()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_cherry", MaterialEffectType.AddFlat, 30f));
            var board = new GpTable(2, 2, null, Cells((0, 0, "m_cherry")));
            Place(board, 1, GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false), 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(40f, result.RawSum, 0.001f);
        }

        [Test]
        public void Walnut_AddsPermanentFlat()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_walnut", MaterialEffectType.PermanentAddFlat, 10f));
            var board = new GpTable(2, 2, null, Cells((0, 0, "m_walnut")));
            DishInstance inst = Place(board, 1, GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false), 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(20f, result.RawSum, 0.001f);
            Assert.IsTrue(result.PermanentFlatDeltas.ContainsKey(inst.Id));
            Assert.AreEqual(10f, result.PermanentFlatDeltas[inst.Id], 0.001f);
        }

        [Test]
        public void Marble_AddsMultiplierFlat()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_marble", MaterialEffectType.AddMultFlat, 3f));
            var board = new GpTable(2, 2, null, Cells((0, 0, "m_marble")));
            Place(board, 1, GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false), 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 倍率 1 + 3 = 4 → 10 * 4 = 40。
            Assert.AreEqual(40f, result.RawSum, 0.001f);
        }

        [Test]
        public void Obsidian_MultipliesContribution()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_obsidian", MaterialEffectType.AddMult, 1.5f));
            var board = new GpTable(2, 2, null, Cells((0, 0, "m_obsidian")));
            Place(board, 1, GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false), 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
        }

        [Test]
        public void Emerald_AddsMultiplierPerOccupiedCell()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_emerald", MaterialEffectType.AddMultFlatPerCell, 0.3f));
            var board = new GpTable(2, 2, null, Cells((0, 0, "m_emerald"), (1, 0, "m_emerald")));
            Place(board, 1, GameplayTestFactory.Dish("d", new[] { "XX" }, deliciousness: 10, allowRotate: false), 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 占 2 格翡翠 → 倍率 1 + 0.3*2 = 1.6 → 10 * 1.6 = 16。
            Assert.AreEqual(16f, result.RawSum, 0.001f);
        }

        [Test]
        public void Gold_GrantsGoldWhenCellCountMeetsThreshold()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_gold", MaterialEffectType.GrantGoldIfCellCount, 10f, "2"));

            var twoCells = new GpTable(2, 2, null, Cells((0, 0, "m_gold"), (1, 0, "m_gold")));
            Place(twoCells, 1, GameplayTestFactory.Dish("d", new[] { "XX" }, deliciousness: 10, allowRotate: false), 0, 0);
            Assert.AreEqual(10f, new ScoreCalculator().Calculate(twoCells, db).GoldDelta, 0.001f);

            var oneCell = new GpTable(2, 2, null, Cells((0, 0, "m_gold")));
            Place(oneCell, 1, GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false), 0, 0);
            Assert.AreEqual(0f, new ScoreCalculator().Calculate(oneCell, db).GoldDelta, 0.001f, "只占 1 格金餐桌不满足阈值");
        }

        [Test]
        public void Silver_RegistersItemRollWhenCellCountMeetsThreshold()
        {
            GameplayDatabase db = Db(GameplayTestFactory.CellMaterial("m_silver", MaterialEffectType.GrantItemRollIfCellCount, 1f, "2"));

            var twoCells = new GpTable(2, 2, null, Cells((0, 0, "m_silver"), (1, 0, "m_silver")));
            Place(twoCells, 1, GameplayTestFactory.Dish("d", new[] { "XX" }, deliciousness: 10, allowRotate: false), 0, 0);
            Assert.AreEqual(1, new ScoreCalculator().Calculate(twoCells, db).SilverItemRollRequests);

            var oneCell = new GpTable(2, 2, null, Cells((0, 0, "m_silver")));
            Place(oneCell, 1, GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false), 0, 0);
            Assert.AreEqual(0, new ScoreCalculator().Calculate(oneCell, db).SilverItemRollRequests, "只占 1 格银餐桌不登记掷骰");
        }

        [Test]
        public void MultipleMaterials_ResolveInBoardOrder()
        {
            GameplayDatabase db = Db(
                GameplayTestFactory.CellMaterial("m_a", MaterialEffectType.AddFlat, 1f),
                GameplayTestFactory.CellMaterial("m_b", MaterialEffectType.AddFlat, 2f));
            var board = new GpTable(2, 1, null, Cells((0, 0, "m_a"), (1, 0, "m_b")));
            Place(board, 1, GameplayTestFactory.Dish("d", new[] { "XX" }, deliciousness: 10, allowRotate: false), 0, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            List<ScoreLine> matLines = result.ScoreLines
                .Where(l => l.Source.Type == ScoreSourceType.Material && l.Kind == ScoreLineKind.DishFlat)
                .ToList();
            Assert.AreEqual(2, matLines.Count);
            Assert.AreEqual("m_a", matLines[0].Source.Id, "靠上左的材质先结算");
            Assert.AreEqual("m_b", matLines[1].Source.Id);
            Assert.AreEqual(13f, result.RawSum, 0.001f);
        }
    }
}
