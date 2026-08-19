using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;

namespace GourmetProject.Tests.EditMode
{
    /// <summary>
    /// 「需确认上菜」开关只由设置决定，不再区分出菜口上菜与临时桌回摆。
    /// </summary>
    public sealed class PendingDishServeConfirmationTests
    {
        [Test]
        public void ShouldAutoConfirmPendingDish_FollowsSettingOnly()
        {
            Assert.That(
                BattleWorldController.ShouldAutoConfirmPendingDish(requireServeConfirmation: false),
                Is.True);
            Assert.That(
                BattleWorldController.ShouldAutoConfirmPendingDish(requireServeConfirmation: true),
                Is.False);
        }

        [Test]
        public void ShouldShowPendingDishActionButton_ComplementsAutoConfirm()
        {
            foreach (bool requireConfirmation in new[] { false, true })
            {
                Assert.That(
                    BattleWorldController.ShouldShowPendingDishActionButton(requireConfirmation),
                    Is.EqualTo(requireConfirmation),
                    $"requireServeConfirmation={requireConfirmation}");
                Assert.That(
                    BattleWorldController.ShouldShowPendingDishActionButton(requireConfirmation),
                    Is.Not.EqualTo(
                        BattleWorldController.ShouldAutoConfirmPendingDish(requireConfirmation)),
                    $"requireServeConfirmation={requireConfirmation}");
            }
        }

        [Test]
        public void PreplaceTemporaryAreaDish_AlreadyServedDish_IsConfirmActionAndCanBeConfirmed()
        {
            DishDef def = Def("cake");
            var table = new DiningTable(4, 4);
            DishInstance dish = Instance(1, def, new GridPos(1, 1));
            table.Place(dish);
            BattleSession session = Session(table, def);

            Assert.That(session.MoveDishToTemporaryAreaAfterRotationDelta(dish.Id, 1), Is.True);
            Assert.That(session.PendingDishPlacements, Is.Empty);

            Placement target = session.FindTemporaryAreaDishPlacements(dish.Id).First();
            Assert.That(session.PreplaceTemporaryAreaDish(dish.Id, target), Is.True);

            PendingDishPlacement pending = session.FindPendingDishPlacement(dish.Id);
            Assert.That(pending, Is.Not.Null);
            Assert.That(pending.ActionKind, Is.EqualTo(PendingDishActionKind.Confirm));
            Assert.That(pending.IsOnDiningTable, Is.True);

            PendingDishConfirmResult confirmed = session.ConfirmPendingDish(dish.Id);

            Assert.That(confirmed.Success, Is.True);
            Assert.That(confirmed.ActionKind, Is.EqualTo(PendingDishActionKind.Confirm));
            Assert.That(session.PendingDishPlacements, Is.Empty);
            Assert.That(session.DiningTable.DishCount, Is.EqualTo(1));
            Assert.That(session.ServesUsed, Is.EqualTo(0));
        }

        [Test]
        public void PreplaceTemporaryAreaDish_UnservedDish_KeepsServeActionAndCountsServe()
        {
            DishDef def = Def("cake");
            var session = new BattleSession(
                new DiningTable(4, 4),
                Database(new[] { def }),
                new Xoshiro256SS(23UL),
                new[] { new RecipeSlot("slot", new[] { def.Id }) },
                requiredScore: 0);

            ServePrepareResult prepared = session.PrepareServe(0);
            Assert.That(prepared.Success, Is.True);
            ServeResult placed = session.PreplacePreparedServe(prepared.PreparedDish.Placements[0]);
            Assert.That(placed.Success, Is.True);

            int dishId = placed.Dish.Id;
            Assert.That(
                session.FindPendingDishPlacement(dishId).ActionKind,
                Is.EqualTo(PendingDishActionKind.Serve));
            Assert.That(session.MoveDishToTemporaryAreaAfterRotationDelta(dishId, 1), Is.True);
            Assert.That(session.FindPendingDishPlacement(dishId).IsOnDiningTable, Is.False);

            Placement target = session.FindTemporaryAreaDishPlacements(dishId).First();
            Assert.That(session.PreplaceTemporaryAreaDish(dishId, target), Is.True);

            PendingDishPlacement pending = session.FindPendingDishPlacement(dishId);
            Assert.That(pending.ActionKind, Is.EqualTo(PendingDishActionKind.Serve));
            Assert.That(pending.IsOnDiningTable, Is.True);

            PendingDishConfirmResult confirmed = session.ConfirmPendingDish(dishId);

            Assert.That(confirmed.Success, Is.True);
            Assert.That(confirmed.ActionKind, Is.EqualTo(PendingDishActionKind.Serve));
            Assert.That(session.PendingDishPlacements, Is.Empty);
            Assert.That(session.ServesUsed, Is.EqualTo(1));
        }

        private static DishDef Def(string id)
            => new DishDef(
                id,
                id,
                10,
                DishShape.FromRows(new[] { "X" }),
                0,
                0,
                1f,
                Array.Empty<string>(),
                string.Empty);

        private static DishInstance Instance(int instanceId, DishDef def, GridPos origin)
            => new DishInstance(
                instanceId,
                def,
                new Placement(def.Shape, 0, origin),
                Array.Empty<string>(),
                Array.Empty<string>());

        private static BattleSession Session(DiningTable table, DishDef def)
            => new BattleSession(
                table,
                Database(new[] { def }),
                new Xoshiro256SS(23UL),
                Array.Empty<RecipeSlot>(),
                requiredScore: 0);

        private static GameplayDatabase Database(IReadOnlyList<DishDef> dishes)
            => new GameplayDatabase(
                dishes,
                Array.Empty<SkillDef>(),
                Array.Empty<FlavorDef>(),
                Array.Empty<RecipeDef>());
    }
}
