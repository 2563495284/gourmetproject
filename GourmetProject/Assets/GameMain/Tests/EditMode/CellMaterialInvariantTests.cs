using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Meta.Passives;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using Luban.SimpleJSON;
using NUnit.Framework;
using UnityEngine;

namespace GourmetProject.Tests.EditMode
{
    public sealed class CellMaterialInvariantTests
    {
        private const string Gold = "m_gold";
        private const string Silver = "m_silver";

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
        public void GeneratedConfiguration_GoldAndSilverMatchCurrentDesign()
        {
            MaterialDef gold = _database.GetMaterial(Gold);
            MaterialDef silver = _database.GetMaterial(Silver);

            Assert.That(gold, Is.Not.Null);
            Assert.That(gold.ThresholdParam, Is.EqualTo(1));
            Assert.That(gold.EffectValue, Is.EqualTo(6f).Within(0.000001f));
            Assert.That(silver, Is.Not.Null);
            Assert.That(silver.ThresholdParam, Is.EqualTo(1));
            Assert.That(silver.ItemRollProbability, Is.EqualTo(0.2f).Within(0.000001f));
        }

        [Test]
        public void DiningTable_ConstructorSetAndCompatibleAdd_KeepOnlyLastMaterial()
        {
            var pos = new GridPos(0, 0);
            var table = new DiningTable(
                1,
                1,
                existingCells: null,
                materials: new Dictionary<GridPos, IReadOnlyList<string>>
                {
                    [pos] = new[] { Gold, string.Empty, Silver },
                });

            Assert.That(table.MaterialsAt(pos), Is.EqualTo(new[] { Silver }));
            Assert.That(table.SetMaterialAt(pos, Silver), Is.False, "相同材质不应被当成一次有效使用");
            Assert.That(table.SetMaterialAt(pos, Gold), Is.True);
            Assert.That(table.MaterialsAt(pos), Is.EqualTo(new[] { Gold }));
            Assert.That(table.AddMaterialAt(pos, Gold), Is.False, "兼容入口也必须拒绝同材质 no-op");
            Assert.That(table.AddMaterialAt(pos, Silver), Is.True);
            Assert.That(table.MaterialsAt(pos), Is.EqualTo(new[] { Silver }));
        }

        [Test]
        public void GameRun_SetAndCompatibleAdd_AreLastWinsWithOneOverridePerCoordinate()
        {
            GameRun run = CreateRun("material-last-wins");
            var pos = new GridPos(3, 4);

            Assert.That(run.SetCellMaterial(pos, Gold), Is.True);
            Assert.That(run.SetCellMaterial(pos, Gold), Is.False);
            Assert.That(run.AddCellMaterial(pos, Silver), Is.True);

            CellMaterialOverride saved = run.CellMaterialOverrides.Single();
            Assert.That(saved.Pos, Is.EqualTo(pos));
            Assert.That(saved.MaterialId, Is.EqualTo(Silver));
        }

        [Test]
        public void LegacySave_DuplicateCoordinatesNormalizeLastWins_AndResaveCompressed()
        {
            RunSaveData legacy = CreateRun("legacy-material-save").ToSaveData();
            legacy.CellMaterialOverrides = new List<CellMaterialSaveData>
            {
                new CellMaterialSaveData { X = 1, Y = 2, MaterialId = Gold },
                new CellMaterialSaveData { X = 5, Y = 6, MaterialId = Gold },
                new CellMaterialSaveData { X = 1, Y = 2, MaterialId = Silver },
                new CellMaterialSaveData { X = 7, Y = 8, MaterialId = string.Empty },
            };

            GameRun restored = GameRun.FromSaveData(_tables, _database, legacy);

            Assert.That(restored.CellMaterialOverrides, Has.Count.EqualTo(2));
            Assert.That(
                restored.CellMaterialOverrides.Single(v => v.Pos.Equals(new GridPos(1, 2))).MaterialId,
                Is.EqualTo(Silver));
            RunSaveData resaved = restored.ToSaveData();
            Assert.That(resaved.CellMaterialOverrides, Has.Count.EqualTo(2));
            Assert.That(
                resaved.CellMaterialOverrides.Single(v => v.X == 1 && v.Y == 2).MaterialId,
                Is.EqualTo(Silver));
        }

        [Test]
        public void Save_DefensivelyCompressesMalformedRuntimeOverrides_LastWins()
        {
            GameRun run = CreateRun("runtime-material-compression");
            var pos = new GridPos(2, 3);
            Assert.That(run.SetCellMaterial(pos, Gold), Is.True);

            var leakedList = run.CellMaterialOverrides as List<CellMaterialOverride>;
            Assert.That(leakedList, Is.Not.Null, "测试需要模拟绕过公开入口的历史脏状态");
            leakedList.Add(new CellMaterialOverride(pos, Silver));

            RunSaveData save = run.ToSaveData();
            Assert.That(save.CellMaterialOverrides, Has.Count.EqualTo(1));
            Assert.That(save.CellMaterialOverrides[0].MaterialId, Is.EqualTo(Silver));
        }

        [Test]
        public void BattleUse_SameMaterialAndInvalidCell_DoNotWriteRun_ReplacementUpdatesBothSides()
        {
            GameRun run = CreateRun("battle-material-atomic");
            var session = run.BuildBattleSession(0, string.Empty, "battle-material-atomic");
            GridPos pos = session.DiningTable.ExistingCells().First();
            session.DiningTable.SetMaterialAt(pos, Gold);
            var target = CellTarget(pos);
            var context = new BattleUseContext(session, run);

            Assert.That(context.AddMaterialToCell(target, Gold), Is.False);
            Assert.That(run.CellMaterialOverrides, Is.Empty, "同材质失败不能先写入 Run");

            Assert.That(context.AddMaterialToCell(target, Silver), Is.True);
            Assert.That(session.DiningTable.MaterialsAt(pos), Is.EqualTo(new[] { Silver }));
            Assert.That(run.CellMaterialOverrides.Single().MaterialId, Is.EqualTo(Silver));

            Assert.That(context.AddMaterialToCell(CellTarget(new GridPos(999, 999)), Gold), Is.False);
            Assert.That(run.CellMaterialOverrides, Has.Count.EqualTo(1));
            Assert.That(session.DiningTable.MaterialsAt(pos), Is.EqualTo(new[] { Silver }));
        }

        [Test]
        public void ShopAndActionContexts_RejectSameMaterialAndReplaceWithoutDuplicateOverrides()
        {
            AssertHeadlessContext(new ShopUseContext(CreateRun("shop-material")));
            AssertHeadlessContext(new ActionSelectUseContext(CreateRun("action-material"), null));
        }

        [Test]
        public void EnumerateTargets_ExcludesSameMaterialButKeepsDifferentMaterialForReplacement()
        {
            ItemDefinition goldItem = ItemDefinition.Get(_tables, "item_active_lay_gold");
            ItemDefinition silverItem = ItemDefinition.Get(_tables, "item_active_lay_silver");
            GameRun run = CreateRun("material-target-filter");
            var session = run.BuildBattleSession(0, string.Empty, "material-target-filter");
            GridPos pos = session.DiningTable.ExistingCells().First();
            session.DiningTable.SetMaterialAt(pos, Gold);
            var context = new BattleUseContext(session, run);

            Assert.That(goldItem, Is.Not.Null);
            Assert.That(silverItem, Is.Not.Null);
            Assert.That(context.EnumerateTargets(goldItem).Any(v => v.X == pos.X && v.Y == pos.Y), Is.False);
            Assert.That(context.EnumerateTargets(silverItem).Any(v => v.X == pos.X && v.Y == pos.Y), Is.True);
        }

        [Test]
        public void SpreadPassive_ReplacesTargetAndReportsSingleMaterialAfterState()
        {
            GameRun run = CreateRun("passive-material-replace");
            DiningTable preview = run.BuildTablePreviewFromFragments();
            List<GridPos> cells = preview.ExistingCells();
            Assert.That(TryFindForwardAdjacentPair(cells, out GridPos source, out GridPos target), Is.True);

            Assert.That(run.SetCellMaterial(source, Gold), Is.True);
            foreach (GridPos neighbor in ExistingNeighbors(preview, source))
            {
                Assert.That(run.SetCellMaterial(neighbor, neighbor.Equals(target) ? Silver : Gold), Is.True);
            }

            CellMutationResult result = PassiveRecipeMutationService.SpreadMaterialToAdjacentCell(
                run,
                "spread",
                new FirstRandomStream(20260814UL));

            CellMutationEntry entry = result.Entries.Single();
            Assert.That(entry.Pos, Is.EqualTo(target));
            Assert.That(entry.MaterialId, Is.EqualTo(Gold));
            Assert.That(entry.BeforeMaterialIds, Is.EqualTo(new[] { Silver }));
            Assert.That(entry.AfterMaterialIds, Is.EqualTo(new[] { Gold }));
            Assert.That(
                run.CellMaterialOverrides.Single(v => v.Pos.Equals(target)).MaterialId,
                Is.EqualTo(Gold));
        }

        [Test]
        public void Scoring_DuplicateMaterialRecordsOnOneCell_CountAsOneCell()
        {
            var pos = new GridPos(0, 0);
            var table = new DiningTable(
                1,
                1,
                existingCells: null,
                materials: new Dictionary<GridPos, IReadOnlyList<string>>
                {
                    [pos] = new[] { Gold },
                });
            var malformed = table.MaterialsAt(pos) as List<string>;
            Assert.That(malformed, Is.Not.Null, "测试需要模拟旧运行时遗留的重复材质");
            malformed.Add(Gold);

            DishShape shape = DishShape.FromRows(new[] { "X" });
            var dishDef = new DishDef(
                "material-count-dish",
                "material-count-dish",
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);
            table.Place(new DishInstance(
                1,
                dishDef,
                new Placement(shape, 0, pos),
                Array.Empty<string>(),
                Array.Empty<string>()));
            var gold = new MaterialDef(
                Gold,
                Gold,
                string.Empty,
                MaterialEffectType.GrantGoldIfCellCount,
                new[] { 15f },
                new[] { "2" },
                string.Empty);
            var database = new GameplayDatabase(
                new[] { dishDef },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                new[] { gold },
                Array.Empty<RecipeDef>());

            ScoreResult score = new ScoreCalculator().Calculate(table, database);

            Assert.That(score.GoldDelta, Is.Zero, "同一格的重复记录不能伪装成占了两格金材质");
        }

        private void AssertHeadlessContext(IActiveUseContext context)
        {
            DiningTable preview = context.Run.BuildTablePreviewFromFragments();
            GridPos pos = preview.ExistingCells().First();
            ActiveTarget target = CellTarget(pos);

            Assert.That(context.AddMaterialToCell(target, Gold), Is.True);
            Assert.That(context.AddMaterialToCell(target, Gold), Is.False);
            Assert.That(context.AddMaterialToCell(target, Silver), Is.True);
            Assert.That(context.Run.CellMaterialOverrides, Has.Count.EqualTo(1));
            Assert.That(context.Run.CellMaterialOverrides[0].MaterialId, Is.EqualTo(Silver));

            var wrongKind = new ActiveTarget(string.Empty, pos.X, pos.Y, cfg.ItemTargetKind.Material);
            Assert.That(context.AddMaterialToCell(wrongKind, Gold), Is.False);
            Assert.That(context.Run.CellMaterialOverrides, Has.Count.EqualTo(1));
        }

        private static ActiveTarget CellTarget(GridPos pos)
            => new ActiveTarget(string.Empty, pos.X, pos.Y, cfg.ItemTargetKind.DiningTableCell);

        private static bool TryFindForwardAdjacentPair(
            IReadOnlyList<GridPos> cells,
            out GridPos source,
            out GridPos target)
        {
            var indexByCell = new Dictionary<GridPos, int>();
            for (int i = 0; i < cells.Count; i++)
            {
                indexByCell[cells[i]] = i;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                foreach (GridPos neighbor in Adjacent(cells[i]))
                {
                    if (indexByCell.TryGetValue(neighbor, out int neighborIndex) && neighborIndex > i)
                    {
                        source = cells[i];
                        target = neighbor;
                        return true;
                    }
                }
            }

            source = default;
            target = default;
            return false;
        }

        private static IEnumerable<GridPos> ExistingNeighbors(DiningTable table, GridPos source)
        {
            foreach (GridPos neighbor in Adjacent(source))
            {
                if (table.Exists(neighbor))
                {
                    yield return neighbor;
                }
            }
        }

        private static IEnumerable<GridPos> Adjacent(GridPos source)
        {
            yield return source.Offset(1, 0);
            yield return source.Offset(-1, 0);
            yield return source.Offset(0, 1);
            yield return source.Offset(0, -1);
        }

        private GameRun CreateRun(string seed)
        {
            return new GameRun(_tables, _database, "glutton_dog", seed, weekIndex: 1);
        }

        private sealed class FirstRandomStream : IRandomStream
        {
            private readonly Xoshiro256SS _inner;

            public FirstRandomStream(ulong seed)
            {
                _inner = new Xoshiro256SS(seed);
            }

            public RngState State
            {
                get => _inner.State;
                set => _inner.State = value;
            }

            public uint NextUInt() => _inner.NextUInt();

            public ulong NextULong() => _inner.NextULong();

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;
        }
    }
}
