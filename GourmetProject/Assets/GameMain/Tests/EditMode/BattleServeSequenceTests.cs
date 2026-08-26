#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleServeSequenceTests
    {
        [Test]
        public void CreateDomainStream_RecreatesSameSequenceAfterCachedCombatStreamAdvances()
        {
            var random = new RandomService();
            random.Init("fixed-serve-sequence");

            IRandomStream combat = random.DomainStream(SeedDomains.Combat, "battle-a");
            combat.NextULong();
            combat.NextULong();

            IRandomStream first = random.CreateDomainStream(
                SeedDomains.Combat,
                "battle-a_serve_sequence");
            ulong[] expected = NextValues(first, 6);

            for (int i = 0; i < 20; i++)
            {
                combat.NextULong();
            }

            IRandomStream replay = random.CreateDomainStream(
                SeedDomains.Combat,
                "battle-a_serve_sequence");
            IRandomStream otherBattle = random.CreateDomainStream(
                SeedDomains.Combat,
                "battle-b_serve_sequence");

            CollectionAssert.AreEqual(expected, NextValues(replay, expected.Length));
            CollectionAssert.AreNotEqual(expected, NextValues(otherBattle, expected.Length));
        }

        [Test]
        public void InitializeSequence_UsesCellWeightsWithoutReplacement_AndServingConsumesNoSequenceRng()
        {
            DishDef oneCell = CreateDish("one", "X");
            DishDef threeCells = CreateDish("three", "XXX");
            var sequenceRng = new RecordingRandomStream(1, 0);
            BattleSession session = CreateSession(
                new DiningTable(4, 4),
                new[] { oneCell, threeCells },
                new[] { "one", "three" },
                sequenceRng);
            session.ConfigureFoodDiscardLimit(2);

            session.InitializeServeSequence();

            Assert.That(sequenceRng.WeightedCalls, Has.Count.EqualTo(2));
            CollectionAssert.AreEqual(new[] { 1f, 3f }, sequenceRng.WeightedCalls[0]);
            CollectionAssert.AreEqual(new[] { 1f }, sequenceRng.WeightedCalls[1]);
            int callsAfterInitialization = sequenceRng.TotalCalls;

            ServePrepareResult first = session.PrepareServeAutomatically(0);
            Assert.That(first.Success, Is.True);
            Assert.That(first.PreparedDish.Definition.Id, Is.EqualTo("three"));
            Assert.That(session.TryDiscardPreparedServe(), Is.True);

            ServePrepareResult second = session.PrepareServeAutomatically(0);
            Assert.That(second.Success, Is.True);
            Assert.That(second.PreparedDish.Definition.Id, Is.EqualTo("one"));
            Assert.That(sequenceRng.TotalCalls, Is.EqualTo(callsAfterInitialization));
        }

        [Test]
        public void PrepareServe_SkipsBlockedHead_ThenRetriesItAfterTableExpands()
        {
            DishDef wide = CreateDish("wide", "XX");
            DishDef small = CreateDish("small", "X");
            var board = new DiningTable(2, 1, new[] { new GridPos(0, 0) });
            BattleSession session = CreateSession(
                board,
                new[] { wide, small },
                new[] { "wide", "small" },
                new RecordingRandomStream(0, 0));
            session.ConfigureFoodDiscardLimit(2);
            session.InitializeServeSequence();

            ServePrepareResult first = session.PrepareServeAutomatically(0);
            Assert.That(first.Success, Is.True);
            Assert.That(first.PreparedDish.Definition.Id, Is.EqualTo("small"));
            Assert.That(session.Slots[0].Remaining, Does.Contain("wide"));
            Assert.That(session.TryDiscardPreparedServe(), Is.True);

            board.SetExists(new GridPos(1, 0), true);

            Assert.That(session.CanServeAny(), Is.True);
            ServePrepareResult recovered = session.PrepareServeAutomatically(0);
            Assert.That(recovered.Success, Is.True);
            Assert.That(recovered.PreparedDish.Definition.Id, Is.EqualTo("wide"));
        }

        [Test]
        public void PrepareServe_AllBlocked_DoesNotMutateOrConsumeRng_AndCanRecover()
        {
            DishDef wide = CreateDish("wide", "XX");
            var board = new DiningTable(2, 1, new[] { new GridPos(0, 0) });
            var sequenceRng = new RecordingRandomStream(0);
            BattleSession session = CreateSession(
                board,
                new[] { wide },
                new[] { "wide" },
                sequenceRng);
            session.InitializeServeSequence();
            int callsAfterInitialization = sequenceRng.TotalCalls;

            Assert.That(session.CanServeAny(), Is.False);
            Assert.That(session.PrepareServeAutomatically(0).Outcome,
                Is.EqualTo(ServePrepareOutcome.NoFittingDish));
            Assert.That(session.PrepareServeAutomatically(0).Outcome,
                Is.EqualTo(ServePrepareOutcome.NoFittingDish));
            Assert.That(session.Slots[0].Count, Is.EqualTo(1));
            Assert.That(sequenceRng.TotalCalls, Is.EqualTo(callsAfterInitialization));

            board.SetExists(new GridPos(1, 0), true);

            Assert.That(session.CanServeAny(), Is.True);
            Assert.That(session.PrepareServeAutomatically(0).PreparedDish.Definition.Id,
                Is.EqualTo("wide"));
        }

        [Test]
        public void InitializeSequence_AppliesFreshPriorityAndPlannedCookiePity()
        {
            var freshFlavor = new FlavorDef(
                "fresh",
                "鲜",
                string.Empty,
                FlavorEffectType.ServePriority,
                Array.Empty<float>(),
                Array.Empty<string>(),
                string.Empty);
            DishDef cookieA = CreateDish("cookie_a", "X");
            DishDef cookieB = CreateDish("cookie_b", "X");
            DishDef normal = CreateDish("normal", "X");
            DishDef fresh = CreateDish("fresh_dish", "X", flavorId: freshFlavor.Id);
            BattleSession session = CreateSession(
                new DiningTable(4, 4),
                new[] { cookieA, cookieB, normal, fresh },
                new[] { "cookie_a", "cookie_b", "normal", "fresh_dish" },
                new RecordingRandomStream(0, 0, 0, 0),
                new[] { freshFlavor });
            session.ConfigureCookieServePity(1, new[] { "cookie_a", "cookie_b" });
            session.ConfigureFoodDiscardLimit(4);
            session.InitializeServeSequence();

            List<string> sequence = DrainPreparedSequence(session);

            Assert.That(sequence, Is.EqualTo(new[]
            {
                "fresh_dish",
                "cookie_a",
                "normal",
                "cookie_b",
            }));
        }

        [Test]
        public void InsertedDish_IsFixedAutomaticToken_SkipsWhenBlocked_AndRetriesLater()
        {
            DishDef inserted = CreateDish("inserted", "XX");
            DishDef normal = CreateDish("normal", "X");
            var board = new DiningTable(2, 1, new[] { new GridPos(0, 0) });
            BattleSession session = CreateSession(
                board,
                new[] { inserted, normal },
                new[] { "normal" },
                new RecordingRandomStream(0));
            session.ConfigureInsertedDishSequence("inserted", windowSize: 2, countPerWindow: 1);
            session.ConfigureFoodDiscardLimit(2);
            session.InitializeServeSequence();

            ServePrepareResult normalResult = session.PrepareServeAutomatically(0);
            Assert.That(normalResult.Success, Is.True);
            Assert.That(normalResult.PreparedDish.Definition.Id, Is.EqualTo("normal"));
            Assert.That(normalResult.PreparedDish.IsBossInsertedDish, Is.False);
            Assert.That(session.TryDiscardPreparedServe(), Is.True);

            board.SetExists(new GridPos(1, 0), true);

            ServePrepareResult insertedResult = session.PrepareServeAutomatically(0);
            Assert.That(insertedResult.Success, Is.True);
            Assert.That(insertedResult.PreparedDish.Definition.Id, Is.EqualTo("inserted"));
            Assert.That(insertedResult.PreparedDish.IsBossInsertedDish, Is.True);
        }

        [Test]
        public void SystemExtraServe_IgnoresAutomaticOnlyInsertedToken()
        {
            DishDef inserted = CreateDish("inserted", "X");
            DishDef normal = CreateDish("normal", "X");
            BattleSession session = CreateSession(
                new DiningTable(2, 1),
                new[] { inserted, normal },
                new[] { "normal" },
                new RecordingRandomStream(0));
            session.ConfigureInsertedDishSequence("inserted", windowSize: 2, countPerWindow: 1);
            session.ConfigureFoodDiscardLimit(2);
            session.InitializeServeSequence();

            ServePrepareResult extraServe = session.PrepareServe(0);
            Assert.That(extraServe.Success, Is.True);
            Assert.That(extraServe.PreparedDish.Definition.Id, Is.EqualTo("normal"));
            Assert.That(extraServe.PreparedDish.IsBossInsertedDish, Is.False);
            Assert.That(session.TryDiscardPreparedServe(), Is.True);

            ServePrepareResult automatic = session.PrepareServeAutomatically(0);
            Assert.That(automatic.Success, Is.True);
            Assert.That(automatic.PreparedDish.Definition.Id, Is.EqualTo("inserted"));
            Assert.That(automatic.PreparedDish.IsBossInsertedDish, Is.True);
        }

        [Test]
        public void FullInsertedWindow_IsBoundedAndLeavesRecipeReachable()
        {
            DishDef inserted = CreateDish("inserted", "X");
            DishDef normal = CreateDish("normal", "X");
            BattleSession session = CreateSession(
                new DiningTable(2, 1),
                new[] { inserted, normal },
                new[] { "normal" },
                new RecordingRandomStream(0));
            session.ConfigureInsertedDishSequence("inserted", windowSize: 1, countPerWindow: 1);
            session.ConfigureFoodDiscardLimit(2);
            session.InitializeServeSequence();

            ServePrepareResult insertedResult = session.PrepareServeAutomatically(0);
            Assert.That(insertedResult.Success, Is.True);
            Assert.That(insertedResult.PreparedDish.IsBossInsertedDish, Is.True);
            Assert.That(session.TryDiscardPreparedServe(), Is.True);

            ServePrepareResult normalResult = session.PrepareServeAutomatically(0);
            Assert.That(normalResult.Success, Is.True);
            Assert.That(normalResult.PreparedDish.Definition.Id, Is.EqualTo("normal"));
            Assert.That(normalResult.PreparedDish.IsBossInsertedDish, Is.False);
        }

        private static BattleSession CreateSession(
            DiningTable board,
            IReadOnlyList<DishDef> dishes,
            IReadOnlyList<string> recipeDishIds,
            IRandomStream sequenceRng,
            IReadOnlyList<FlavorDef> flavors = null)
        {
            var database = new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                flavors ?? Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            var slot = new RecipeSlot("recipe", recipeDishIds);
            return new BattleSession(
                board,
                database,
                new Xoshiro256SS(12345UL),
                new[] { slot },
                requiredScore: 0,
                serveSequenceRng: sequenceRng);
        }

        private static DishDef CreateDish(string id, string row, string flavorId = "")
            => new DishDef(
                id,
                id,
                deliciousness: 1,
                DishShape.FromRows(new[] { row }),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                Array.Empty<string>(),
                flavorId);

        private static List<string> DrainPreparedSequence(BattleSession session)
        {
            var result = new List<string>();
            while (true)
            {
                ServePrepareResult prepared = session.PrepareServeAutomatically(0);
                if (prepared.Outcome == ServePrepareOutcome.SlotEmpty)
                {
                    break;
                }

                Assert.That(prepared.Success, Is.True, $"Unexpected outcome: {prepared.Outcome}");
                result.Add(prepared.PreparedDish.Definition.Id);
                Assert.That(session.TryDiscardPreparedServe(), Is.True);
            }

            return result;
        }

        private static ulong[] NextValues(IRandomStream stream, int count)
        {
            var values = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = stream.NextULong();
            }

            return values;
        }

        private sealed class RecordingRandomStream : IRandomStream
        {
            private readonly Queue<int> _weightedIndices;

            public RecordingRandomStream(params int[] weightedIndices)
            {
                _weightedIndices = new Queue<int>(weightedIndices ?? Array.Empty<int>());
            }

            public List<float[]> WeightedCalls { get; } = new List<float[]>();

            public int ShuffleCalls { get; private set; }

            public int TotalCalls => WeightedCalls.Count + ShuffleCalls;

            public RngState State { get; set; }

            public uint NextUInt() => 0u;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5d) => probability > 0d;

            public void Shuffle<T>(IList<T> list)
            {
                ShuffleCalls++;
            }

            public T Pick<T>(IReadOnlyList<T> list) => list[0];

            public int WeightedPickIndex(IReadOnlyList<float> weights)
            {
                WeightedCalls.Add(weights.ToArray());
                int requested = _weightedIndices.Count > 0 ? _weightedIndices.Dequeue() : 0;
                return Math.Max(0, Math.Min(weights.Count - 1, requested));
            }
        }
    }
}
#endif
