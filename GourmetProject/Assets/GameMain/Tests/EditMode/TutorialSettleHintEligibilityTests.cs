using System;
using GourmetProject.Core.Rng;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    public sealed class TutorialSettleHintEligibilityTests
    {
        [Test]
        public void RecipeEmpty_IsEligibleWhenNothingCanBeServed()
        {
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                Dish("single", "X"),
                Array.Empty<string>());

            Assert.That(session.CanServeAny(), Is.False);
            Assert.That(IsEligible(canServeAny: session.CanServeAny()), Is.True);
        }

        [Test]
        public void RemainingFoodCannotFit_IsEligibleWhenNothingCanBeServed()
        {
            DishDef oversized = Dish("oversized", "XX");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                oversized,
                new[] { oversized.Id });

            Assert.That(session.Slots[0].Count, Is.EqualTo(1));
            Assert.That(session.CanServeAny(), Is.False);
            Assert.That(IsEligible(canServeAny: session.CanServeAny()), Is.True);
        }

        [Test]
        public void ServeLimitReached_IsEligibleWhenNothingCanBeServed()
        {
            DishDef dish = Dish("limited", "X");
            BattleSession session = CreateSession(
                new DiningTable(2, 1),
                dish,
                new[] { dish.Id, dish.Id });
            session.MaxServes = 1;

            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);
            ServeResult placed = session.CommitPreparedServe(prepared.PreparedDish.Placements[0]);
            Assert.That(placed.Success, Is.True);
            Assert.That(session.ServesUsed, Is.EqualTo(session.MaxServes));
            Assert.That(session.Slots[0].Count, Is.EqualTo(1));
            Assert.That(session.CanServeAny(), Is.False);
            Assert.That(IsEligible(canServeAny: session.CanServeAny()), Is.True);
        }

        [Test]
        public void ServeableFoodRemains_IsNotEligible()
        {
            DishDef dish = Dish("serveable", "X");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                dish,
                new[] { dish.Id });

            Assert.That(session.CanServeAny(), Is.True);
            Assert.That(IsEligible(canServeAny: session.CanServeAny()), Is.False);
        }

        [Test]
        public void PreparedServe_IsNotEligible()
        {
            DishDef dish = Dish("prepared", "X");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                dish,
                new[] { dish.Id });

            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);
            Assert.That(session.PreparedServe, Is.Not.Null);
            Assert.That(session.CanServeAny(), Is.True);
            Assert.That(IsEligible(canServeAny: session.CanServeAny()), Is.False);
        }

        [Test]
        public void PendingFoodCanStillBeConfirmed_IsNotEligible()
        {
            DishDef dish = Dish("pending", "X");
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                dish,
                new[] { dish.Id });
            ServePrepareResult prepared = session.PrepareServeAutomatically(0);
            Assert.That(prepared.Success, Is.True);

            ServeResult placed = session.PreplacePreparedServe(prepared.PreparedDish.Placements[0]);
            Assert.That(placed.Success, Is.True);
            Assert.That(session.PreparedServe, Is.Null);
            Assert.That(session.HasPendingTablePlacements, Is.True);
            Assert.That(session.CanServeAny(), Is.True);
            Assert.That(IsEligible(canServeAny: session.CanServeAny()), Is.False);
        }

        [Test]
        public void NonTutorialBattle_IsNotEligible()
        {
            Assert.That(IsEligible(isFirstTutorialBattle: false), Is.False);
        }

        [Test]
        public void MissingSession_IsNotEligible()
        {
            Assert.That(IsEligible(hasSession: false), Is.False);
        }

        [Test]
        public void SettledBattle_IsNotEligible()
        {
            Assert.That(IsEligible(isSettled: true), Is.False);
        }

        [Test]
        public void InitialBattleTutorialNotCompleted_IsNotEligible()
        {
            Assert.That(IsEligible(firstBattleCompleted: false), Is.False);
        }

        [Test]
        public void SettleHintAlreadyCompleted_IsNotEligible()
        {
            Assert.That(IsEligible(settleHintCompleted: true), Is.False);
        }

        [Test]
        public void AnotherTutorialIsPlaying_IsNotEligible()
        {
            Assert.That(IsEligible(tutorialPlaying: true), Is.False);
        }

        [Test]
        public void FoodInteractionIsBusy_IsNotEligible()
        {
            Assert.That(IsEligible(foodInteractionBusy: true), Is.False);
        }

        [Test]
        public void TemporaryAreaFoodRemains_IsNotEligible()
        {
            Assert.That(IsEligible(hasTemporaryAreaDishes: true), Is.False);
        }

        private static bool IsEligible(
            bool isFirstTutorialBattle = true,
            bool hasSession = true,
            bool isSettled = false,
            bool firstBattleCompleted = true,
            bool settleHintCompleted = false,
            bool tutorialPlaying = false,
            bool foodInteractionBusy = false,
            bool hasTemporaryAreaDishes = false,
            bool canServeAny = false)
        {
            return BattleForm.ShouldPlayFirstBattleSettleHint(
                isFirstTutorialBattle,
                hasSession,
                isSettled,
                firstBattleCompleted,
                settleHintCompleted,
                tutorialPlaying,
                foodInteractionBusy,
                hasTemporaryAreaDishes,
                canServeAny);
        }

        private static BattleSession CreateSession(
            DiningTable table,
            DishDef dish,
            string[] recipeDishIds)
        {
            var database = new GameplayDatabase(
                new[] { dish },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(240814UL),
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
    }
}
