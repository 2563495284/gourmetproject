using System;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattleFoodDiscardTests
    {
        [Test]
        public void TryDiscardPlacedDish_RemovesDishAndConsumesDiscard()
        {
            var table = new DiningTable(2, 2);
            BattleSession session = CreateSession(table);
            session.ConfigureFoodDiscardLimit(1);
            DishInstance dish = CreatePlacedDish(table, 1, new GridPos(0, 0));

            bool discarded = session.TryDiscardPlacedDish(dish);

            Assert.That(discarded, Is.True);
            Assert.That(table.DishCount, Is.Zero);
            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsRemaining, Is.Zero);
        }

        [Test]
        public void TryDiscardPlacedDish_WhenDishIsNotOnTable_DoesNotConsumeDiscard()
        {
            var table = new DiningTable(2, 2);
            BattleSession session = CreateSession(table);
            session.ConfigureFoodDiscardLimit(1);
            DishInstance dish = CreateDish(1, new GridPos(0, 0));

            bool discarded = session.TryDiscardPlacedDish(dish);

            Assert.That(discarded, Is.False);
            Assert.That(session.FoodDiscardsUsed, Is.Zero);
            Assert.That(session.FoodDiscardsRemaining, Is.EqualTo(1));
        }

        [Test]
        public void AdjustFoodDiscardLimit_DuringBattle_AddsToMaximumAndRemaining()
        {
            var table = new DiningTable(2, 2);
            BattleSession session = CreateSession(table);
            session.ConfigureFoodDiscardLimit(1);
            DishInstance dish = CreatePlacedDish(table, 1, new GridPos(0, 0));
            Assert.That(session.TryDiscardPlacedDish(dish), Is.True);
            Assert.That(session.FoodDiscardsRemaining, Is.Zero);

            session.AdjustFoodDiscardLimit(2);

            Assert.That(session.FoodDiscardLimit, Is.EqualTo(3));
            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsRemaining, Is.EqualTo(2));
        }

        [Test]
        public void AdjustFoodDiscardLimit_AfterBattleOverride_AddsOnlyRuntimeDelta()
        {
            BattleSession session = CreateSession(new DiningTable(2, 2));
            session.ConfigureFoodDiscardLimit(3);
            session.ConfigureFoodDiscardLimit(0); // 例如 Boss 将本场初始丢弃次数压到 0。

            session.AdjustFoodDiscardLimit(1);

            Assert.That(session.FoodDiscardLimit, Is.EqualTo(1));
            Assert.That(session.FoodDiscardsUsed, Is.Zero);
            Assert.That(session.FoodDiscardsRemaining, Is.EqualTo(1));
        }

        [Test]
        public void NewBattleSession_StartsWithFullConfiguredDiscardCapacity()
        {
            var firstTable = new DiningTable(2, 2);
            BattleSession first = CreateSession(firstTable);
            first.ConfigureFoodDiscardLimit(2);
            Assert.That(
                first.TryDiscardPlacedDish(CreatePlacedDish(firstTable, 1, new GridPos(0, 0))),
                Is.True);
            Assert.That(first.FoodDiscardsRemaining, Is.EqualTo(1));

            BattleSession next = CreateSession(new DiningTable(2, 2));
            next.ConfigureFoodDiscardLimit(2);

            Assert.That(next.FoodDiscardLimit, Is.EqualTo(2));
            Assert.That(next.FoodDiscardsUsed, Is.Zero);
            Assert.That(next.FoodDiscardsRemaining, Is.EqualTo(2));
        }

        private static BattleSession CreateSession(DiningTable table)
        {
            var database = new GameplayDatabase(
                Array.Empty<DishDef>(),
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(1UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);
        }

        private static DishInstance CreatePlacedDish(
            DiningTable table,
            int instanceId,
            GridPos origin)
        {
            DishInstance dish = CreateDish(instanceId, origin);
            table.Place(dish);
            return dish;
        }

        private static DishInstance CreateDish(int instanceId, GridPos origin)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var definition = new DishDef(
                "test_dish",
                "测试食物",
                1,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
            var placement = new Placement(shape, 0, origin);
            return new DishInstance(
                instanceId,
                definition,
                placement,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
