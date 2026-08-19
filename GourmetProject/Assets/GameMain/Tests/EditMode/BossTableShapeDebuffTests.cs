using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// 放纵餐 / 暴食餐 / 儿童餐 / 减脂餐必须改玩家当前餐桌，不能先改最大包围盒再按默认桌重造。
    /// </summary>
    public sealed class BossTableShapeDebuffTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfiguration()
        {
            string configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            _tables = new cfg.Tables(name =>
                JSON.Parse(File.ReadAllText(Path.Combine(configDirectory, name + ".json"))));
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void TableShapeDebuffs_ModifyCurrentCustomTableInsteadOfDefaultBounds()
        {
            GameRun run = new GameRun(_tables, _database, "glutton_dog", "table-shape-debuff", weekIndex: 1);
            cfg.Character character = _tables.TbCharacter.Get(run.CharacterId);
            DiningTable baseline = run.BuildTablePreviewFromFragments();
            Assert.That(TryPlaceExtraFragment(run, baseline, character, out _), Is.True);

            DiningTable custom = run.BuildTablePreviewFromFragments();
            HashSet<GridPos> customCells = new HashSet<GridPos>(custom.ExistingCells());
            Assert.That(custom.CellCapacity, Is.GreaterThan(baseline.CellCapacity));

            DiningTable indulgent = run.BuildTablePreviewFromFragments(bossDebuffId: "debuff_indulgent");
            DiningTable binge = run.BuildTablePreviewFromFragments(bossDebuffId: "debuff_binge");
            DiningTable kidsMeal = run.BuildTablePreviewFromFragments(bossDebuffId: "debuff_kids_meal");
            DiningTable weightLoss = run.BuildTablePreviewFromFragments(bossDebuffId: "debuff_weight_loss");

            AssertSameCanvas(custom, indulgent, "debuff_indulgent");
            AssertSameCanvas(custom, binge, "debuff_binge");
            AssertSameCanvas(custom, kidsMeal, "debuff_kids_meal");
            AssertSameCanvas(custom, weightLoss, "debuff_weight_loss");

            HashSet<GridPos> expectedAddedBottom = ExpectedEdgeAdds(customCells, horizontal: false);
            HashSet<GridPos> expectedAddedRight = ExpectedEdgeAdds(customCells, horizontal: true);
            HashSet<GridPos> expectedRemovedBottom = ExpectedEdgeRemoves(customCells, horizontal: false);
            HashSet<GridPos> expectedRemovedRight = ExpectedEdgeRemoves(customCells, horizontal: true);

            Assert.That(CellSet(indulgent), Is.EquivalentTo(Union(customCells, expectedAddedBottom)));
            Assert.That(CellSet(binge), Is.EquivalentTo(Union(customCells, expectedAddedRight)));
            Assert.That(CellSet(kidsMeal), Is.EquivalentTo(Except(customCells, expectedRemovedBottom)));
            Assert.That(CellSet(weightLoss), Is.EquivalentTo(Except(customCells, expectedRemovedRight)));
        }

        private bool TryPlaceExtraFragment(
            GameRun run,
            DiningTable baseline,
            cfg.Character character,
            out GridPos origin)
        {
            origin = default;
            string[] candidates = { "frag_3_1", "frag_3_3", "frag_4_1", "frag_2_1" };
            foreach (string fragmentId in candidates)
            {
                TableFragmentDef fragment = _database.GetFragment(fragmentId);
                if (fragment == null
                    || !TryFindPlacement(
                        baseline,
                        fragment,
                        character.MaxDiningTableWidth,
                        character.MaxDiningTableHeight,
                        out origin))
                {
                    continue;
                }

                Assert.That(run.AddFragmentPlacement(fragmentId, 0, origin), Is.True);
                return true;
            }

            return false;
        }

        private static bool TryFindPlacement(
            DiningTable table,
            TableFragmentDef fragment,
            int maxWidth,
            int maxHeight,
            out GridPos origin)
        {
            origin = default;
            HashSet<GridPos> existing = TableFragmentBuilder.ToExistingSet(table);
            if (!table.TryGetExistingBounds(out int minX, out int minY, out int maxX, out int maxY))
            {
                return false;
            }

            for (int y = minY - maxHeight; y <= maxY + maxHeight; y++)
            {
                for (int x = minX - maxWidth; x <= maxX + maxWidth; x++)
                {
                    var candidate = new GridPos(x, y);
                    if (TableFragmentBuilder.GetFragmentPlacementStatusWithinMaxBounds(
                            existing,
                            fragment,
                            candidate,
                            maxWidth,
                            maxHeight)
                        == TableFragmentBuilder.FragmentPlacementStatus.Valid)
                    {
                        origin = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AssertSameCanvas(DiningTable expected, DiningTable actual, string debuffId)
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width), debuffId + " 不应改画布宽");
            Assert.That(actual.Height, Is.EqualTo(expected.Height), debuffId + " 不应改画布高");
        }

        private static HashSet<GridPos> CellSet(DiningTable table) => new HashSet<GridPos>(table.ExistingCells());

        private static HashSet<GridPos> Union(HashSet<GridPos> source, HashSet<GridPos> extra)
        {
            var result = new HashSet<GridPos>(source);
            result.UnionWith(extra);
            return result;
        }

        private static HashSet<GridPos> Except(HashSet<GridPos> source, HashSet<GridPos> removed)
        {
            var result = new HashSet<GridPos>(source);
            result.ExceptWith(removed);
            return result;
        }

        private static HashSet<GridPos> ExpectedEdgeAdds(HashSet<GridPos> cells, bool horizontal)
        {
            var result = new HashSet<GridPos>();
            foreach (IGrouping<int, GridPos> group in cells.GroupBy(cell => horizontal ? cell.Y : cell.X))
            {
                result.Add(horizontal
                    ? new GridPos(group.Max(cell => cell.X) + 1, group.Key)
                    : new GridPos(group.Key, group.Max(cell => cell.Y) + 1));
            }

            return result;
        }

        private static HashSet<GridPos> ExpectedEdgeRemoves(HashSet<GridPos> cells, bool horizontal)
        {
            var result = new HashSet<GridPos>();
            foreach (IGrouping<int, GridPos> group in cells.GroupBy(cell => horizontal ? cell.Y : cell.X))
            {
                result.Add(horizontal
                    ? new GridPos(group.Max(cell => cell.X), group.Key)
                    : new GridPos(group.Key, group.Max(cell => cell.Y)));
            }

            return result;
        }
    }
}
