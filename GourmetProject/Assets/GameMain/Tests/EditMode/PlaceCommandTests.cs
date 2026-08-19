using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.DevConsole.Commands;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class PlaceCommandTests
    {
        [Test]
        public void Fill_PlacesUntilRemainingFoodCannotFit()
        {
            DishDef bite = Dish("bite", "X");
            BattleSession session = CreateSession(
                new DiningTable(2, 2),
                bite,
                Repeat(bite.Id, 6));

            RandomPlaceResult result = PlaceCommand.Fill(session, new ScriptedRandom());

            Assert.That(result.PlacedCount, Is.EqualTo(4));
            Assert.That(result.StopReason, Is.EqualTo(RandomPlaceStopReason.OutletEmpty));
            Assert.That(result.LastPrepareOutcome, Is.EqualTo(ServePrepareOutcome.NoFittingDish));
            Assert.That(result.OutletHasUnplaceableDish, Is.False);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(4));
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.Slots[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void Fill_LeavesOutletDishWhenCurrentPreparedDishNoLongerFits()
        {
            DishDef platter = Dish("platter", "XX", "XX");
            DishDef bite = Dish("bite", "X");
            BattleSession session = CreateSession(
                new DiningTable(2, 2),
                new[] { platter, bite },
                new[] { platter.Id },
                battleSeed: 9UL);
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);
            Assert.That(session.GenerateDishAt(bite.Id, new GridPos(0, 0)), Is.True);

            RandomPlaceResult result = PlaceCommand.Fill(session, new ScriptedRandom());

            Assert.That(result.PlacedCount, Is.EqualTo(0));
            Assert.That(result.StopReason, Is.EqualTo(RandomPlaceStopReason.CannotPlace));
            Assert.That(result.OutletHasUnplaceableDish, Is.True);
            Assert.That(session.PreparedServe, Is.Not.Null);
            Assert.That(session.PreparedServe.Definition.Id, Is.EqualTo(platter.Id));
        }

        [Test]
        public void Fill_StopsWhenOutletHasNoFood()
        {
            DishDef bite = Dish("bite", "X");
            BattleSession session = CreateSession(
                new DiningTable(4, 4),
                bite,
                Repeat(bite.Id, 3));

            RandomPlaceResult result = PlaceCommand.Fill(session, new ScriptedRandom());

            Assert.That(result.PlacedCount, Is.EqualTo(3));
            Assert.That(result.StopReason, Is.EqualTo(RandomPlaceStopReason.OutletEmpty));
            Assert.That(result.OutletHasUnplaceableDish, Is.False);
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.Slots[0].IsEmpty, Is.True);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(3));
        }

        [Test]
        public void Fill_KeepsOutletDrawSequenceWhenPlacementStreamChanges()
        {
            DishDef small = Dish("small", "X");
            DishDef longDish = Dish("long", "XX");
            var dishes = new[] { small, longDish };
            string[] recipe = { small.Id, longDish.Id, small.Id, longDish.Id, small.Id };
            BattleSession first = CreateSession(new DiningTable(4, 4), dishes, recipe, battleSeed: 11UL);
            BattleSession second = CreateSession(new DiningTable(4, 4), dishes, recipe, battleSeed: 11UL);

            PlaceCommand.Fill(first, new ScriptedRandom(0, 0, 0, 0, 0));
            PlaceCommand.Fill(second, new ScriptedRandom(3, 2, 1, 4, 0));

            Assert.That(DishIds(second), Is.EqualTo(DishIds(first)));
        }

        [Test]
        public void Fill_UsesPlacementStreamToChooseOrigin()
        {
            DishDef bite = Dish("bite", "X");
            BattleSession firstOrigin = CreateSession(
                new DiningTable(2, 2),
                bite,
                new[] { bite.Id },
                battleSeed: 3UL);
            BattleSession lastOrigin = CreateSession(
                new DiningTable(2, 2),
                bite,
                new[] { bite.Id },
                battleSeed: 3UL);

            PlaceCommand.Fill(firstOrigin, new ScriptedRandom(0));
            PlaceCommand.Fill(lastOrigin, new ScriptedRandom(3));

            Assert.That(firstOrigin.DiningTable.Dishes[0].Placement.Origin, Is.EqualTo(new GridPos(0, 0)));
            Assert.That(lastOrigin.DiningTable.Dishes[0].Placement.Origin, Is.EqualTo(new GridPos(1, 1)));
        }

        [Test]
        public void Fill_ConfirmsPendingOutletDishThenContinues()
        {
            DishDef bite = Dish("bite", "X");
            BattleSession session = CreateSession(
                new DiningTable(2, 1),
                bite,
                Repeat(bite.Id, 2));
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);
            Assert.That(session.PreplacePreparedServe(prepared.PreparedDish.Placements[0]).Success, Is.True);
            Assert.That(session.HasPendingTablePlacements, Is.True);

            RandomPlaceResult result = PlaceCommand.Fill(session, new ScriptedRandom());

            Assert.That(result.ConfirmedPendingCount, Is.EqualTo(1));
            Assert.That(result.PlacedCount, Is.EqualTo(1));
            Assert.That(result.StopReason, Is.EqualTo(RandomPlaceStopReason.OutletEmpty));
            Assert.That(session.HasPendingTablePlacements, Is.False);
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(2));
        }

        private static List<string> DishIds(BattleSession session)
        {
            var ids = new List<string>(session.DiningTable.DishCount);
            foreach (DishInstance dish in session.DiningTable.Dishes)
            {
                ids.Add(dish.Def.Id);
            }

            return ids;
        }

        private static BattleSession CreateSession(
            DiningTable table,
            DishDef dish,
            string[] recipeDishIds,
            ulong battleSeed = 240814UL)
        {
            return CreateSession(table, new[] { dish }, recipeDishIds, battleSeed);
        }

        private static BattleSession CreateSession(
            DiningTable table,
            IReadOnlyList<DishDef> dishes,
            string[] recipeDishIds,
            ulong battleSeed)
        {
            var database = new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(battleSeed),
                new[] { new RecipeSlot("slot", recipeDishIds) },
                requiredScore: 0);
        }

        private static DishDef Dish(string id, params string[] rows)
        {
            return new DishDef(
                id,
                id,
                deliciousness: 1,
                shape: DishShape.FromRows(rows),
                hiddenMin: 0,
                hiddenMax: 0,
                baseWeight: 1f,
                skillIds: Array.Empty<string>(),
                flavorId: string.Empty);
        }

        private static string[] Repeat(string id, int count)
        {
            var ids = new string[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = id;
            }

            return ids;
        }

        private sealed class ScriptedRandom : IRandomStream
        {
            private readonly Queue<int> _pickIndices;

            public ScriptedRandom(params int[] pickIndices)
            {
                _pickIndices = new Queue<int>(pickIndices ?? Array.Empty<int>());
            }

            public RngState State
            {
                get => default;
                set { }
            }

            public uint NextUInt() => 0;

            public ulong NextULong() => 0UL;

            public int Range(int minInclusive, int maxExclusive) => minInclusive;

            public float Range(float minInclusive, float maxExclusive) => minInclusive;

            public float NextFloat() => 0f;

            public double NextDouble() => 0d;

            public bool NextBool(double probability = 0.5) => false;

            public void Shuffle<T>(IList<T> list)
            {
            }

            public T Pick<T>(IReadOnlyList<T> list)
            {
                if (list == null || list.Count == 0)
                {
                    throw new InvalidOperationException("Cannot pick from an empty list.");
                }

                int index = _pickIndices.Count > 0 ? _pickIndices.Dequeue() : 0;
                if (index < 0)
                {
                    index = 0;
                }
                else if (index >= list.Count)
                {
                    index = list.Count - 1;
                }

                return list[index];
            }

            public int WeightedPickIndex(IReadOnlyList<float> weights) => 0;
        }
    }
}
