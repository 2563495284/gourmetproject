using System;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class BattlePendingPlacementTests
    {
        [Test]
        public void PreparedDish_PreplaceDefersServeAndGoldUntilConfirmed()
        {
            BattleSession session = CreateSession(entryCount: 2);
            session.GoldCostPerConfirmedServe = 5;
            int servedEvents = 0;
            session.Served += (_, _) => servedEvents++;

            PreparedServeDish prepared = session.PrepareServeAutomatically(0).PreparedDish;
            ServeResult placed = session.PreplacePreparedServe(prepared.Placements[0]);

            Assert.That(placed.Success, Is.True);
            Assert.That(session.HasPendingTablePlacements, Is.True);
            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(session.PendingGold, Is.Zero);
            Assert.That(servedEvents, Is.Zero);

            PendingDishConfirmResult confirmed = session.ConfirmPendingDish(placed.Dish.Id);

            Assert.That(confirmed.Success, Is.True);
            Assert.That(confirmed.ActionKind, Is.EqualTo(PendingDishActionKind.Serve));
            Assert.That(session.HasPendingTablePlacements, Is.False);
            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(session.PendingGold, Is.EqualTo(-5));
            Assert.That(servedEvents, Is.EqualTo(1));
        }

        [Test]
        public void AutomaticPrepare_WaitsUntilEveryTablePlacementIsConfirmed()
        {
            BattleSession session = CreateSession(entryCount: 2);
            PreparedServeDish first = session.PrepareServeAutomatically(0).PreparedDish;
            ServeResult placed = session.PreplacePreparedServe(first.Placements[0]);

            ServePrepareResult blocked = session.PrepareServeAutomatically(0);

            Assert.That(blocked.Outcome, Is.EqualTo(ServePrepareOutcome.PendingPlacement));
            Assert.That(session.ConfirmPendingDish(placed.Dish.Id).Success, Is.True);
            Assert.That(session.PrepareServeAutomatically(0).Success, Is.True);
        }

        [Test]
        public void DiscardingPreplacedServe_DoesNotServeOrChargeGold()
        {
            BattleSession session = CreateSession(entryCount: 1);
            session.ConfigureFoodDiscardLimit(1);
            session.GoldCostPerConfirmedServe = 5;
            PreparedServeDish prepared = session.PrepareServeAutomatically(0).PreparedDish;
            ServeResult placed = session.PreplacePreparedServe(prepared.Placements[0]);

            Assert.That(session.TryDiscardPlacedDish(placed.Dish), Is.True);

            Assert.That(session.ServesUsed, Is.Zero);
            Assert.That(session.PendingGold, Is.Zero);
            Assert.That(session.HasPendingTablePlacements, Is.False);
            Assert.That(session.DiningTable.DishCount, Is.Zero);
        }

        [Test]
        public void TemporaryAreaPlacement_RequiresConfirmWithoutAdditionalServe()
        {
            BattleSession session = CreateSession(entryCount: 1);
            ServeResult served = PrepareAndCommit(session);
            Assert.That(session.MoveDishToTemporaryAreaAfterRotate(served.Dish.Id, 1), Is.True);

            Placement placement = session.FindTemporaryAreaDishPlacements(served.Dish.Id).First();
            Assert.That(session.PreplaceTemporaryAreaDish(served.Dish.Id, placement), Is.True);
            PendingDishPlacement pending = session.FindPendingDishPlacement(served.Dish.Id);

            Assert.That(pending.ActionKind, Is.EqualTo(PendingDishActionKind.Confirm));
            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(session.ConfirmPendingDish(served.Dish.Id).Success, Is.True);
            Assert.That(session.ServesUsed, Is.EqualTo(1));
        }

        [Test]
        public void Settle_ConfirmsOnlyPendingDishesAlreadyOnDiningTable()
        {
            BattleSession session = CreateSession(entryCount: 3);
            ServeResult served = PrepareAndCommit(session);
            Assert.That(session.MoveDishToTemporaryAreaAfterRotate(served.Dish.Id, 1), Is.True);

            PreparedServeDish pendingTable = session.PrepareServe(0).PreparedDish;
            Assert.That(session.PreplacePreparedServe(pendingTable.Placements[0]).Success, Is.True);
            PreparedServeDish waitingAtOutlet = session.PrepareServe(0).PreparedDish;

            session.Settle();

            Assert.That(session.ServesUsed, Is.EqualTo(2));
            Assert.That(session.PreparedServe, Is.SameAs(waitingAtOutlet));
            Assert.That(session.TemporaryAreaDishes, Is.EqualTo(new[] { served.Dish }));
            Assert.That(session.HasPendingTablePlacements, Is.False);
        }

        [Test]
        public void TemporaryAreaDish_CanBeDiscardedDirectly()
        {
            BattleSession session = CreateSession(entryCount: 1);
            session.ConfigureFoodDiscardLimit(1);
            ServeResult served = PrepareAndCommit(session);
            Assert.That(session.MoveDishToTemporaryAreaAfterRotate(served.Dish.Id, 1), Is.True);

            Assert.That(session.TryDiscardTemporaryAreaDish(served.Dish.Id), Is.True);
            Assert.That(session.TemporaryAreaDishes, Is.Empty);
            Assert.That(session.FoodDiscardsUsed, Is.EqualTo(1));
        }

        [Test]
        public void Settle_WhenPendingServeIsRemovedAsAppetizer_StillCompletesSettlement()
        {
            BattleSession session = CreateSession(entryCount: 1);
            session.RemoveFirstServedDishes = true;
            session.FirstServedDishesToRemove = 1;
            PreparedServeDish prepared = session.PrepareServeAutomatically(0).PreparedDish;
            Assert.That(session.PreplacePreparedServe(prepared.Placements[0]).Success, Is.True);

            session.Settle();

            Assert.That(session.IsSettled, Is.True);
            Assert.That(session.ServesUsed, Is.EqualTo(1));
            Assert.That(session.DiningTable.DishCount, Is.Zero);
            Assert.That(session.HasPendingTablePlacements, Is.False);
            Assert.That(session.PrepareServeAutomatically(0).Success, Is.False);
        }

        private static ServeResult PrepareAndCommit(BattleSession session)
        {
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            Assert.That(prepared, Is.Not.Null);
            ServeResult result = session.CommitPreparedServe(prepared.Placements[0]);
            Assert.That(result.Success, Is.True);
            return result;
        }

        private static BattleSession CreateSession(int entryCount)
        {
            DishShape shape = DishShape.FromRows(new[] { "X" });
            var dish = new DishDef(
                "dish",
                "测试菜",
                10,
                shape,
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
            var database = new GameplayDatabase(
                new[] { dish },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            var slot = new RecipeSlot(
                "recipe",
                Enumerable.Range(0, entryCount)
                    .Select(_ => new RecipeSlotEntry(dish.Id)));
            return new BattleSession(
                new DiningTable(6, 2),
                database,
                new Xoshiro256SS(7UL),
                new[] { slot },
                requiredScore: 0);
        }
    }
}
