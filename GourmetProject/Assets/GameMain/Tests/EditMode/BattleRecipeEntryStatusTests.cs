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
    public sealed class BattleRecipeEntryStatusTests
    {
        [Test]
        public void BattleRecipeEntries_RetainIndependentBossDuplicatedEntries()
        {
            BattleSession session = CreateSession(
                new DiningTable(2, 1),
                new RecipeSlotEntry("dish"),
                new RecipeSlotEntry("dish"));

            Assert.That(
                session.GetBattleRecipeEntries(0).Select(entry => entry.Status),
                Is.All.EqualTo(BattleRecipeEntryStatus.Normal));

            Assert.That(session.PrepareServe(0).Success, Is.True);

            IReadOnlyList<BattleRecipeEntrySnapshot> entries =
                session.GetBattleRecipeEntries(0);
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(
                entries.Count(entry =>
                    entry.Status == BattleRecipeEntryStatus.WaitingForPlacement),
                Is.EqualTo(1));
            Assert.That(
                entries.Count(entry =>
                    entry.Status == BattleRecipeEntryStatus.Normal),
                Is.EqualTo(1));
            Assert.That(
                entries.Select(entry => entry.EntryId).Distinct().Count(),
                Is.EqualTo(2));
        }

        [Test]
        public void PreparedDish_TransitionsToServedThenDiscarded()
        {
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new RecipeSlotEntry("dish"));
            session.ConfigureFoodDiscardLimit(1);

            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            Assert.That(
                Status(session),
                Is.EqualTo(BattleRecipeEntryStatus.WaitingForPlacement));

            ServeResult served = session.CommitPreparedServe(prepared.Placements[0]);
            Assert.That(served.Success, Is.True);
            Assert.That(
                Status(session),
                Is.EqualTo(BattleRecipeEntryStatus.Served));

            Assert.That(session.TryDiscardPlacedDish(served.Dish), Is.True);
            Assert.That(
                Status(session),
                Is.EqualTo(BattleRecipeEntryStatus.Discarded));
        }

        [Test]
        public void PreparedDish_DiscardedFromOutlet_RemainsInCompleteRecipe()
        {
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new RecipeSlotEntry("dish"));
            session.ConfigureFoodDiscardLimit(1);

            Assert.That(session.PrepareServe(0).Success, Is.True);
            Assert.That(session.TryDiscardPreparedServe(), Is.True);

            IReadOnlyList<BattleRecipeEntrySnapshot> entries =
                session.GetBattleRecipeEntries(0);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(
                entries[0].Status,
                Is.EqualTo(BattleRecipeEntryStatus.Discarded));
        }

        [Test]
        public void ServedDish_DestroyedOrCleared_TransitionsToRemoved()
        {
            BattleSession destroyedSession = CreateSession(
                new DiningTable(1, 1),
                new RecipeSlotEntry("dish"));
            ServeResult destroyedDish = PrepareAndCommit(destroyedSession);

            Assert.That(
                destroyedSession.DestroyDishById(destroyedDish.Dish.Id),
                Is.True);
            Assert.That(
                Status(destroyedSession),
                Is.EqualTo(BattleRecipeEntryStatus.Removed));

            BattleSession clearedSession = CreateSession(
                new DiningTable(1, 1),
                new RecipeSlotEntry("dish"));
            PrepareAndCommit(clearedSession);
            clearedSession.ClearBoard();

            Assert.That(
                Status(clearedSession),
                Is.EqualTo(BattleRecipeEntryStatus.Removed));
        }

        [Test]
        public void BossImmediateRemoval_TransitionsDirectlyToRemoved()
        {
            BattleSession session = CreateSession(
                new DiningTable(1, 1),
                new RecipeSlotEntry("dish"));
            session.RemoveFirstServedDishes = true;
            session.FirstServedDishesToRemove = 1;

            ServeResult result = PrepareAndCommit(session);

            Assert.That(result.RemovedAfterServe, Is.True);
            Assert.That(
                Status(session),
                Is.EqualTo(BattleRecipeEntryStatus.Removed));
        }

        [Test]
        public void CannotPlace_RepresentsNoSpaceAndServeLimit()
        {
            DiningTable blockedTable = new DiningTable(1, 1);
            BattleSession blockedSession = CreateSession(
                blockedTable,
                new RecipeSlotEntry("dish"));
            blockedTable.Place(CreateDishInstance(
                blockedSession.Database.GetDish("dish"),
                99,
                new GridPos(0, 0)));

            Assert.That(
                Status(blockedSession),
                Is.EqualTo(BattleRecipeEntryStatus.CannotPlace));

            BattleSession limitedSession = CreateSession(
                new DiningTable(1, 1),
                new RecipeSlotEntry("dish"));
            limitedSession.MaxServes = 0;

            Assert.That(
                Status(limitedSession),
                Is.EqualTo(BattleRecipeEntryStatus.CannotPlace));
        }

        private static BattleRecipeEntryStatus Status(BattleSession session)
        {
            IReadOnlyList<BattleRecipeEntrySnapshot> entries =
                session.GetBattleRecipeEntries(0);
            Assert.That(entries.Count, Is.EqualTo(1));
            return entries[0].Status;
        }

        private static ServeResult PrepareAndCommit(BattleSession session)
        {
            PreparedServeDish prepared = session.PrepareServe(0).PreparedDish;
            Assert.That(prepared, Is.Not.Null);
            ServeResult result =
                session.CommitPreparedServe(prepared.Placements[0]);
            Assert.That(result.Success, Is.True);
            return result;
        }

        private static BattleSession CreateSession(
            DiningTable table,
            params RecipeSlotEntry[] entries)
        {
            DishDef definition = CreateDishDefinition();
            var database = new GameplayDatabase(
                new[] { definition },
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<MaterialDef>(),
                Array.Empty<RecipeDef>());
            return new BattleSession(
                table,
                database,
                new Xoshiro256SS(1UL),
                new[] { new RecipeSlot("recipe", entries) },
                requiredScore: 0);
        }

        private static DishDef CreateDishDefinition()
        {
            return new DishDef(
                "dish",
                "测试菜",
                1,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty,
                allowRotate: false);
        }

        private static DishInstance CreateDishInstance(
            DishDef definition,
            int id,
            GridPos origin)
        {
            var placement = new Placement(definition.Shape, 0, origin);
            return new DishInstance(
                id,
                definition,
                placement,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
