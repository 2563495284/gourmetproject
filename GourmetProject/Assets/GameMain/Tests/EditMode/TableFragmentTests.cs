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
    /// <summary>不规则胃部棋盘三态、格子强化标签结算统一、以及 TableFragmentBuilder 造盘测试。</summary>
    public class TableFragmentTests
    {
        private static GameplayDatabase Db(
            IEnumerable<SkillDef> skills = null,
            IEnumerable<MaterialDef> materials = null)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                skills ?? new List<SkillDef>(),
                new List<FlavorDef>(),
                materials ?? new List<MaterialDef>(),
                new List<RecipeDef>());
        }

        private static Dictionary<GridPos, IReadOnlyList<string>> CellMaterials(params (int x, int y, string tag)[] entries)
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
            var board = new GpTable(2, 2, existing, null);

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
            var board = new GpTable(2, 2, existing, null);
            DishDef single = GameplayTestFactory.Dish("s", new[] { "X" }, allowRotate: false);

            board.Place(GameplayTestFactory.Instance(1, single, 0, 0));

            // 3 存在 - 1 占用 = 2 空。
            Assert.AreEqual(2, board.EmptyCellCount);
        }

        [Test]
        public void CellTag_AppliesToOccupyingDish()
        {
            GameplayDatabase db = Db(materials: new[] { GameplayTestFactory.CellMaterial("gold", MaterialEffectType.AddMult, 2f) });
            var board = new GpTable(2, 2, null, CellMaterials((0, 0, "gold")));
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new string[0]));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 菜自身无标签，占据的格挂 gold(×2)：10 * 2 = 20。
            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        [Test]
        public void Material_AppliesOncePerDishRegardlessOfCellCount()
        {
            GameplayDatabase db = Db(materials: new[] { GameplayTestFactory.CellMaterial("gold", MaterialEffectType.AddMult, 2f) });
            var board = new GpTable(2, 2, null, CellMaterials((0, 0, "gold"), (1, 0, "gold")));
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "XX" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new string[0]));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 材质按「食物×材质」聚合：占 2 格 gold 也只结算一次 ×2 → 10 * 2 = 20。
            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        [Test]
        public void CellTag_CombinesWithDishOwnTag()
        {
            GameplayDatabase db = Db(
                skills: new[] { GameplayTestFactory.Skill("fresh", FlavorEffectType.AddFlat, 5f) },
                materials: new[] { GameplayTestFactory.CellMaterial("gold", MaterialEffectType.AddMult, 2f) });
            var board = new GpTable(2, 2, null, CellMaterials((0, 0, "gold")));
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "fresh" }));

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // (10 自身 + 5 fresh) * 2 gold = 30。
            Assert.AreEqual(30f, result.RawSum, 0.001f);
        }

        [Test]
        public void StomachBuilder_BuildsExistingCellsAndCellTags()
        {
            var fragment = new TableFragmentDef(
                "frag_L",
                new[] { "XX", ".X" },
                0, 0, 0f, 0,
                new[] { new CellMaterial(new GridPos(1, 1), "gold") });

            DiningTable board = TableFragmentBuilder.BuildInitial(fragment, 3, 3);

            Assert.AreEqual(3, board.Width);
            Assert.AreEqual(3, board.Height);
            Assert.IsTrue(board.Exists(new GridPos(0, 0)));
            Assert.IsTrue(board.Exists(new GridPos(1, 0)));
            Assert.IsTrue(board.Exists(new GridPos(1, 1)));
            Assert.IsFalse(board.Exists(new GridPos(0, 1))); // 形状中 (0,1) 为 '.'
            Assert.AreEqual(3, board.CellCapacity);
            CollectionAssert.Contains(board.MaterialsAt(new GridPos(1, 1)).ToList(), "gold");
            Assert.AreEqual(0, board.MaterialsAt(new GridPos(0, 0)).Count);
        }

        [Test]
        public void StomachFragmentDef_Rotated_RotatesShapeAndTags()
        {
            // 水平 2x1（右格挂 tag）顺时针旋转 90° → 竖直 1x2，tag 跟随到 (0,1)。
            var fragment = new TableFragmentDef(
                "t", new[] { "XX" }, 0, 0, 0f, 0,
                new[] { new CellMaterial(new GridPos(1, 0), "g") });

            TableFragmentDef rotated = fragment.Rotated(1);

            Assert.AreEqual(2, rotated.ShapeRows.Count);
            Assert.AreEqual("X", rotated.ShapeRows[0]);
            Assert.AreEqual("X", rotated.ShapeRows[1]);
            Assert.AreEqual(1, rotated.CellMaterials.Count);
            Assert.AreEqual(new GridPos(0, 1), rotated.CellMaterials[0].Pos);
            Assert.AreEqual("g", rotated.CellMaterials[0].MaterialId);
        }

        [Test]
        public void StomachFragmentDef_Rotated_FourTimesReturnsOriginalShape()
        {
            var fragment = new TableFragmentDef(
                "t", new[] { "XX", ".X" }, 0, 0, 0f, 0,
                System.Array.Empty<CellMaterial>());

            TableFragmentDef back = fragment.Rotated(4);

            CollectionAssert.AreEqual(fragment.ShapeRows.ToList(), back.ShapeRows.ToList());
        }

        [Test]
        public void StomachBuilder_CanPlaceFragmentAt_ChecksBoundsOverlapAdjacency()
        {
            var existing = new HashSet<GridPos>
            {
                new GridPos(0, 0), new GridPos(1, 0), new GridPos(0, 1), new GridPos(1, 1),
            };
            var single = new TableFragmentDef("d", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());

            // 贴边相邻、在界内、不重叠 → 合法。
            Assert.IsTrue(TableFragmentBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(2, 0), 4, 4));
            // 与已有胃重叠 → 非法。
            Assert.IsFalse(TableFragmentBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(0, 0), 4, 4));
            // 不相邻（悬空）→ 非法。
            Assert.IsFalse(TableFragmentBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(3, 3), 4, 4));
            // 越界 → 非法。
            Assert.IsFalse(TableFragmentBuilder.CanPlaceFragmentAt(existing, single, 0, new GridPos(4, 0), 4, 4));
        }

        [Test]
        public void StomachBuilder_BuildFromPlacements_AddsAdjacentFragment()
        {
            var initial = new TableFragmentDef("gut", new[] { "XX", "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var ext = new TableFragmentDef("ext", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var placements = new[] { new TableFragmentPlacement("ext", 0, new GridPos(2, 0)) };

            DiningTable board = TableFragmentBuilder.BuildFromPlacements(
                initial, placements, id => id == "ext" ? ext : null, 4, 4);

            Assert.AreEqual(5, board.CellCapacity);
            Assert.IsTrue(board.Exists(new GridPos(2, 0)));
            Assert.IsFalse(board.Exists(new GridPos(2, 1)));
        }

        [Test]
        public void StomachBuilder_BuildFromPlacements_SkipsIllegalPlacement()
        {
            var initial = new TableFragmentDef("gut", new[] { "XX", "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var ext = new TableFragmentDef("ext", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            // 悬空放置（不相邻）应被跳过。
            var placements = new[] { new TableFragmentPlacement("ext", 0, new GridPos(3, 3)) };

            DiningTable board = TableFragmentBuilder.BuildFromPlacements(
                initial, placements, id => id == "ext" ? ext : null, 4, 4);

            Assert.AreEqual(4, board.CellCapacity);
            Assert.IsFalse(board.Exists(new GridPos(3, 3)));
        }

        [Test]
        public void StomachBuilder_CenteredOrigin_AllowsExpansionAroundInitialBoard()
        {
            var initial = new TableFragmentDef("gut", new[] { "XX", "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var single = new TableFragmentDef("ext", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());

            GridPos origin = TableFragmentBuilder.CenteredOrigin(initial, 4, 4);
            DiningTable board = TableFragmentBuilder.BuildFromExpanded(initial, null, null, null, 4, 4, origin);

            Assert.AreEqual(new GridPos(1, 1), origin);
            Assert.IsFalse(board.Exists(new GridPos(0, 0)));
            Assert.IsTrue(board.Exists(new GridPos(1, 1)));
            Assert.IsTrue(board.Exists(new GridPos(2, 2)));
            Assert.IsTrue(TableFragmentBuilder.CanPlaceFragmentAt(board, single, 0, new GridPos(0, 1)), "left expansion should be legal");
            Assert.IsTrue(TableFragmentBuilder.CanPlaceFragmentAt(board, single, 0, new GridPos(1, 0)), "top expansion should be legal");
            Assert.IsFalse(TableFragmentBuilder.CanPlaceFragmentAt(board, single, 0, new GridPos(-1, 1)), "out-of-bounds expansion should be rejected");
        }

        [Test]
        public void StomachBuilder_LocalBounds_AllowsExpansionUntilMergedBboxExceedsMaxWidth()
        {
            var existing = new HashSet<GridPos>
            {
                new GridPos(10, 10),
                new GridPos(11, 10),
                new GridPos(12, 10),
                new GridPos(13, 10),
                new GridPos(14, 10),
                new GridPos(15, 10),
            };
            var twoWide = new TableFragmentDef("ext", new[] { "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var threeWide = new TableFragmentDef("ext_big", new[] { "XXX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());

            Assert.AreEqual(
                TableFragmentBuilder.FragmentPlacementStatus.Valid,
                TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(existing, twoWide, new GridPos(16, 10), 8, 4),
                "current width 6 + right expansion 2 should still fit max width 8");
            Assert.AreEqual(
                TableFragmentBuilder.FragmentPlacementStatus.Valid,
                TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(existing, twoWide, new GridPos(8, 10), 8, 4),
                "same expansion to the left should also fit max width 8");
            Assert.AreEqual(
                TableFragmentBuilder.FragmentPlacementStatus.OutOfBounds,
                TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(existing, threeWide, new GridPos(16, 10), 8, 4),
                "merged width 9 should exceed max width 8");
        }

        [Test]
        public void StomachBuilder_LocalBounds_DistinguishesInvalidReasons()
        {
            var existing = new HashSet<GridPos>
            {
                new GridPos(10, 10),
                new GridPos(11, 10),
                new GridPos(12, 10),
                new GridPos(13, 10),
                new GridPos(14, 10),
                new GridPos(15, 10),
            };
            var single = new TableFragmentDef("ext", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var threeWide = new TableFragmentDef("ext_big", new[] { "XXX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());

            Assert.AreEqual(TableFragmentBuilder.FragmentPlacementStatus.Overlap, TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(existing, single, new GridPos(10, 10), 8, 4));
            Assert.AreEqual(TableFragmentBuilder.FragmentPlacementStatus.Detached, TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(existing, single, new GridPos(18, 10), 8, 4));
            Assert.AreEqual(TableFragmentBuilder.FragmentPlacementStatus.OutOfBounds, TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(existing, threeWide, new GridPos(16, 10), 8, 4));
        }

        [Test]
        public void StomachBuilder_LocalBounds_IgnoresSavedFragmentRotation()
        {
            var initial = new TableFragmentDef("gut", new[] { "X" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var ext = new TableFragmentDef("ext", new[] { "XX" }, 0, 0, 0f, 0, System.Array.Empty<CellMaterial>());
            var placements = new[] { new TableFragmentPlacement("ext", 1, new GridPos(3, 2)) };

            DiningTable board = TableFragmentBuilder.BuildFromExpandedLocalBounds(
                initial,
                null,
                placements,
                id => id == "ext" ? ext : null,
                4,
                4,
                8,
                8,
                new GridPos(2, 2));

            Assert.IsTrue(board.Exists(new GridPos(3, 2)));
            Assert.IsTrue(board.Exists(new GridPos(4, 2)), "rotation should be ignored, keeping the horizontal shape");
            Assert.IsFalse(board.Exists(new GridPos(3, 3)), "rotated vertical footprint should not be applied");
        }

        [Test]
        public void StomachBuilder_ClampsFragmentToMaxBounds()
        {
            // 4x4 满碎片塞进 3x3 包围盒：超出部分裁掉，剩 9 格。
            var fragment = new TableFragmentDef(
                "gut_4x4",
                new[] { "XXXX", "XXXX", "XXXX", "XXXX" },
                0, 0, 0f, 0,
                System.Array.Empty<CellMaterial>());

            DiningTable board = TableFragmentBuilder.BuildInitial(fragment, 3, 3);

            Assert.AreEqual(9, board.CellCapacity);
            Assert.IsFalse(board.Exists(new GridPos(3, 3)));
        }
    }
}
