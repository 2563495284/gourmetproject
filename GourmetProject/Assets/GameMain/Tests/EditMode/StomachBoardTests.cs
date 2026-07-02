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
    /// <summary>不规则胃部棋盘三态、格子强化标签结算统一、以及 StomachBuilder 造盘测试。</summary>
    public class StomachBoardTests
    {
        private static GameplayDatabase Db(
            IEnumerable<SkillDef> skills = null,
            IEnumerable<CellTagDef> cellTags = null)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                skills ?? new List<SkillDef>(),
                new List<FlavorDef>(),
                cellTags ?? new List<CellTagDef>(),
                new List<RecipeDef>());
        }

        private static Dictionary<GridPos, IReadOnlyList<string>> CellTags(params (int x, int y, string tag)[] entries)
        {
            var map = new Dictionary<GridPos, IReadOnlyList<string>>();
            foreach ((int x, int y, string tag) in entries)
            {
                map[new GridPos(x, y)] = new List<string> { tag };
            }

            return map;
        }

        [Test]
        public void VoidCell_NotExists_NotEmpty_CannotPlace()
        {
            // 2x2 包围盒，仅 3 格存在，(1,1) 为胃外虚格。
            var existing = new[] { new GridPos(0, 0), new GridPos(1, 0), new GridPos(0, 1) };
            var board = new GpBoard(2, 2, existing, null);

            Assert.IsFalse(board.Exists(new GridPos(1, 1)));
            Assert.IsTrue(board.Exists(new GridPos(0, 0)));
            Assert.AreEqual(3, board.CellCapacity);
            Assert.AreEqual(3, board.EmptyCellCount);

            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, allowRotate: false);
            Assert.IsFalse(board.CanPlace(single.Shape, new GridPos(1, 1)));
            Assert.IsTrue(board.CanPlace(single.Shape, new GridPos(0, 0)));
        }

        [Test]
        public void EmptyCellCount_ExcludesVoidAndOccupied()
        {
            var existing = new[] { new GridPos(0, 0), new GridPos(1, 0), new GridPos(0, 1) };
            var board = new GpBoard(2, 2, existing, null);
            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, allowRotate: false);

            board.Place(GameplayTestFactory.Instance(1, single, 0, 0));

            // 3 存在 - 1 占用 = 2 空。
            Assert.AreEqual(2, board.EmptyCellCount);
        }

        [Test]
        public void CellTag_AppliesToOccupyingDish()
        {
            GameplayDatabase db = Db(cellTags: new[] { GameplayTestFactory.CellTag("gold", TagEffectType.AddMult, 2f) });
            var board = new GpBoard(2, 2, null, CellTags((0, 0, "gold")));
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new string[0]));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 菜自身无标签，占据的格挂 gold(×2)：10 * 2 = 20。
            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        [Test]
        public void CellTag_StacksPerOccupiedTaggedCell()
        {
            GameplayDatabase db = Db(cellTags: new[] { GameplayTestFactory.CellTag("gold", TagEffectType.AddMult, 2f) });
            var board = new GpBoard(2, 2, null, CellTags((0, 0, "gold"), (1, 0, "gold")));
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "XX" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new string[0]));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 两个 gold 各乘一次：10 * 2 * 2 = 40。
            Assert.AreEqual(40f, result.RawSum, 0.001f);
        }

        [Test]
        public void CellTag_CombinesWithDishOwnTag()
        {
            GameplayDatabase db = Db(
                skills: new[] { GameplayTestFactory.Skill("fresh", TagEffectType.AddFlat, 5f) },
                cellTags: new[] { GameplayTestFactory.CellTag("gold", TagEffectType.AddMult, 2f) });
            var board = new GpBoard(2, 2, null, CellTags((0, 0, "gold")));
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // (10 自身 + 5 fresh) * 2 gold = 30。
            Assert.AreEqual(30f, result.RawSum, 0.001f);
        }

        [Test]
        public void StomachBuilder_BuildsExistingCellsAndCellTags()
        {
            var fragment = new StomachFragmentDef(
                "frag_L",
                new[] { "XX", ".X" },
                0, 0, 0f, 0,
                new[] { new CellTag(new GridPos(1, 1), "gold") });

            Board board = StomachBuilder.BuildInitial(fragment, 3, 3);

            Assert.AreEqual(3, board.Width);
            Assert.AreEqual(3, board.Height);
            Assert.IsTrue(board.Exists(new GridPos(0, 0)));
            Assert.IsTrue(board.Exists(new GridPos(1, 0)));
            Assert.IsTrue(board.Exists(new GridPos(1, 1)));
            Assert.IsFalse(board.Exists(new GridPos(0, 1))); // 形状中 (0,1) 为 '.'
            Assert.AreEqual(3, board.CellCapacity);
            CollectionAssert.Contains(board.TagsAt(new GridPos(1, 1)).ToList(), "gold");
            Assert.AreEqual(0, board.TagsAt(new GridPos(0, 0)).Count);
        }

        [Test]
        public void StomachFragmentDef_Rotated_RotatesShapeAndTags()
        {
            // 水平 2x1（右格挂 tag）顺时针旋转 90° → 竖直 1x2，tag 跟随到 (0,1)。
            var fragment = new StomachFragmentDef(
                "t", new[] { "XX" }, 0, 0, 0f, 0,
                new[] { new CellTag(new GridPos(1, 0), "g") });

            StomachFragmentDef rotated = fragment.Rotated(1);

            Assert.AreEqual(2, rotated.ShapeRows.Count);
            Assert.AreEqual("X", rotated.ShapeRows[0]);
            Assert.AreEqual("X", rotated.ShapeRows[1]);
            Assert.AreEqual(1, rotated.CellTags.Count);
            Assert.AreEqual(new GridPos(0, 1), rotated.CellTags[0].Pos);
            Assert.AreEqual("g", rotated.CellTags[0].TagId);
        }

        [Test]
        public void StomachFragmentDef_Rotated_FourTimesReturnsOriginalShape()
        {
            var fragment = new StomachFragmentDef(
                "t", new[] { "XX", ".X" }, 0, 0, 0f, 0,
                System.Array.Empty<CellTag>());

            StomachFragmentDef back = fragment.Rotated(4);

            CollectionAssert.AreEqual(fragment.ShapeRows.ToList(), back.ShapeRows.ToList());
        }

        [Test]
        public void StomachBuilder_CanPlaceFragmentAt_ChecksBoundsOverlapAdjacency()
        {
            var existing = new HashSet<GridPos>
            {
                new GridPos(0, 0), new GridPos(1, 0), new GridPos(0, 1), new GridPos(1, 1),
            };
            var single = new StomachFragmentDef("d", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellTag>());

            // 贴边相邻、在界内、不重叠 → 合法。
            Assert.IsTrue(StomachBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(2, 0), 4, 4));
            // 与已有胃重叠 → 非法。
            Assert.IsFalse(StomachBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(0, 0), 4, 4));
            // 不相邻（悬空）→ 非法。
            Assert.IsFalse(StomachBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(3, 3), 4, 4));
            // 越界 → 非法。
            Assert.IsFalse(StomachBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(4, 0), 4, 4));
        }

        [Test]
        public void StomachBuilder_BuildFromPlacements_AddsAdjacentFragment()
        {
            var initial = new StomachFragmentDef("gut", new[] { "XX", "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellTag>());
            var ext = new StomachFragmentDef("ext", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellTag>());
            var placements = new[] { new StomachFragmentPlacement("ext", 0, new GridPos(2, 0)) };

            Board board = StomachBuilder.BuildFromPlacements(
                initial, placements, id => id == "ext" ? ext : null, 4, 4);

            Assert.AreEqual(5, board.CellCapacity);
            Assert.IsTrue(board.Exists(new GridPos(2, 0)));
            Assert.IsFalse(board.Exists(new GridPos(2, 1)));
        }

        [Test]
        public void StomachBuilder_BuildFromPlacements_SkipsIllegalPlacement()
        {
            var initial = new StomachFragmentDef("gut", new[] { "XX", "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellTag>());
            var ext = new StomachFragmentDef("ext", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellTag>());
            // 悬空放置（不相邻）应被跳过。
            var placements = new[] { new StomachFragmentPlacement("ext", 0, new GridPos(3, 3)) };

            Board board = StomachBuilder.BuildFromPlacements(
                initial, placements, id => id == "ext" ? ext : null, 4, 4);

            Assert.AreEqual(4, board.CellCapacity);
            Assert.IsFalse(board.Exists(new GridPos(3, 3)));
        }

        [Test]
        public void StomachBuilder_ClampsFragmentToMaxBounds()
        {
            // 4x4 满碎片塞进 3x3 包围盒：超出部分裁掉，剩 9 格。
            var fragment = new StomachFragmentDef(
                "gut_4x4",
                new[] { "XXXX", "XXXX", "XXXX", "XXXX" },
                0, 0, 0f, 0,
                System.Array.Empty<CellTag>());

            Board board = StomachBuilder.BuildInitial(fragment, 3, 3);

            Assert.AreEqual(9, board.CellCapacity);
            Assert.IsFalse(board.Exists(new GridPos(3, 3)));
        }
    }
}
