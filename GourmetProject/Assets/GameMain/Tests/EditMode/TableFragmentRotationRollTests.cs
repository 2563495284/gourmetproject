using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Config;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TableFragmentRotationRollTests
    {
        private cfg.Tables _tables;
        private GameplayDatabase _database;

        [OneTimeSetUp]
        public void LoadConfig()
        {
            var config = new ConfigService();
            config.LoadAll();
            _tables = config.Tables;
            _database = GameplayContentBuilder.BuildDatabase(_tables);
        }

        [Test]
        public void SetPendingFragmentPack_RollsEachCandidateDirectionIndependently()
        {
            GameRun run = CreateRun();
            string[] ids = _database.AllFragments.Take(4).Select(fragment => fragment.Id).ToArray();
            Assume.That(ids, Has.Length.EqualTo(4));

            run.SetPendingFragmentPack(
                ids,
                new Xoshiro256SS(123UL),
                new SequenceRandomStream(0, 1, 2, 3));

            Assert.That(run.PendingFragmentPack, Is.EqualTo(ids));
            Assert.That(run.PendingFragmentPackRotations, Is.EqualTo(new[] { 0, 3, 2, 1 }));
        }

        [Test]
        public void PendingFragmentRotations_RoundTripAndReachChoiceRequest()
        {
            GameRun run = CreateRun();
            string[] ids = _database.AllFragments.Take(4).Select(fragment => fragment.Id).ToArray();
            Assume.That(ids, Has.Length.EqualTo(4));
            run.SetPendingFragmentPack(
                ids,
                new Xoshiro256SS(456UL),
                new SequenceRandomStream(3, 2, 1, 0));

            GameRun restored = GameRun.FromSaveData(_tables, _database, run.ToSaveData());
            var request = new TableFragmentChoiceRequest(
                restored,
                restored.PendingFragmentPack,
                null,
                null,
                restored.PendingFragmentPackRotations);

            Assert.That(restored.PendingFragmentPackRotations, Is.EqualTo(new[] { 1, 2, 3, 0 }));
            Assert.That(request.CandidateRotations, Is.EqualTo(restored.PendingFragmentPackRotations));
        }

        [Test]
        public void PendingFragmentRotations_OldSaveWithoutDirectionsDefaultsToZero()
        {
            GameRun run = CreateRun();
            string[] ids = _database.AllFragments.Take(2).Select(fragment => fragment.Id).ToArray();
            Assume.That(ids, Has.Length.EqualTo(2));
            RunSaveData save = run.ToSaveData();
            save.PendingFragmentPackIds = new List<string>(ids);
            save.PendingFragmentPackRotations = null;

            GameRun restored = GameRun.FromSaveData(_tables, _database, save);

            Assert.That(restored.PendingFragmentPackRotations, Is.EqualTo(new[] { 0, 0 }));
        }

        [Test]
        public void ExplicitRewardChoiceRotations_AreNotRerolledWhenPackIsOpened()
        {
            GameRun run = CreateRun();
            string[] ids = _database.AllFragments.Take(4).Select(fragment => fragment.Id).ToArray();
            Assume.That(ids, Has.Length.EqualTo(4));
            var choices = new List<RewardChoice>
            {
                FragmentChoice(ids[0], 0),
                FragmentChoice(ids[1], 3),
                FragmentChoice(ids[2], 2),
                FragmentChoice(ids[3], 1),
            };

            RewardGranter.ApplyFragmentPack(run, choices);

            Assert.That(run.PendingFragmentPackRotations, Is.EqualTo(new[] { 0, 3, 2, 1 }));
        }

        [Test]
        public void BuildFromExpandedLocalBounds_RebuildsPlacedFragmentWithSavedRotation()
        {
            var initial = new TableFragmentDef(
                "initial",
                new[] { "X" },
                0,
                0,
                1f,
                Array.Empty<string>(),
                Array.Empty<CellMaterial>());
            var fragment = new TableFragmentDef(
                "asymmetric_l",
                new[] { "XX", "X." },
                0,
                0,
                1f,
                Array.Empty<string>(),
                new[] { new CellMaterial(new GridPos(1, 0), "material_test") });
            var placement = new TableFragmentPlacement(
                fragment.Id,
                rotation: 1,
                origin: new GridPos(4, 5));

            DiningTable board = TableFragmentBuilder.BuildFromExpandedLocalBounds(
                initial,
                null,
                new[] { placement },
                id => id == fragment.Id ? fragment : null,
                maxWidth: 4,
                maxHeight: 4,
                canvasWidth: 12,
                canvasHeight: 12,
                initialOrigin: new GridPos(4, 4));

            Assert.That(board.Exists(new GridPos(5, 6)), Is.True, "旋转后的右下格应进入最终餐桌");
            Assert.That(board.Exists(new GridPos(4, 6)), Is.False, "旋转前才存在的左下格不应进入最终餐桌");
            Assert.That(board.MaterialsAt(new GridPos(5, 6)), Does.Contain("material_test"));
        }

        private static RewardChoice FragmentChoice(string id, int rotation)
        {
            return new RewardChoice(
                cfg.RewardKind.FragmentChoice,
                id,
                id,
                "扩展餐桌",
                fragmentRotation: rotation);
        }

        private GameRun CreateRun()
        {
            string characterId = _tables.TbCharacter.DataList.First().Id;
            return new GameRun(_tables, _database, characterId, "table-fragment-rotation-tests");
        }

        private sealed class SequenceRandomStream : IRandomStream
        {
            private readonly Queue<int> _values;

            public SequenceRandomStream(params int[] values)
            {
                _values = new Queue<int>(values);
            }

            public RngState State { get; set; }

            public int Range(int minInclusive, int maxExclusive)
            {
                int value = _values.Dequeue();
                Assert.That(value, Is.InRange(minInclusive, maxExclusive - 1));
                return value;
            }

            public uint NextUInt() => throw new NotSupportedException();
            public ulong NextULong() => throw new NotSupportedException();
            public float Range(float minInclusive, float maxExclusive) => throw new NotSupportedException();
            public float NextFloat() => throw new NotSupportedException();
            public double NextDouble() => throw new NotSupportedException();
            public bool NextBool(double probability = 0.5) => throw new NotSupportedException();
            public void Shuffle<T>(IList<T> list) => throw new NotSupportedException();
            public T Pick<T>(IReadOnlyList<T> list) => throw new NotSupportedException();
            public int WeightedPickIndex(IReadOnlyList<float> weights) => throw new NotSupportedException();
        }
    }
}
