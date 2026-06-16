using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using NUnit.Framework;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Tests
{
    /// <summary>局内战斗会话测试：上菜确定性、棋盘填充、结算达标判定。</summary>
    public class BattleSessionTests
    {
        private static GameplayDatabase BuildDb()
        {
            var dishes = new List<DishDef>
            {
                GameplayTestFactory.Dish("rice", new[] { "X" }, deliciousness: 5, allowRotate: false),
                GameplayTestFactory.Dish("egg", new[] { "XX" }, deliciousness: 8, allowRotate: true),
            };
            return new GameplayDatabase(dishes, new List<TagDef>(), new List<RecipeDef>());
        }

        private static BattleSession BuildSession(RandomService rng, int requiredScore)
        {
            GameplayDatabase db = BuildDb();
            var slots = new List<RecipeSlot>
            {
                new RecipeSlot("slot0", new[] { "rice", "egg", "rice", "egg", "rice" }),
            };
            return new BattleSession(new GpBoard(4, 4), db, rng.Stream("battle"), slots, requiredScore);
        }

        [Test]
        public void Serve_PlacesDishAndConsumesFromSlot()
        {
            var rng = new RandomService();
            rng.Init("battle-seed");
            BattleSession session = BuildSession(rng, requiredScore: 1);

            int before = session.Slots[0].Count;
            ServeResult result = session.Serve(0);

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.Dish);
            Assert.AreEqual(1, session.Board.DishCount);
            Assert.AreEqual(before - 1, session.Slots[0].Count);
        }

        [Test]
        public void Serve_AllowsRotationEvenWhenDishConfigDisallowsIt()
        {
            var rng = new RandomService();
            rng.Init("serve-rotate");
            DishDef bar = GameplayTestFactory.Dish("bar", new[] { "XX" }, allowRotate: false);
            var db = new GameplayDatabase(new[] { bar }, new List<TagDef>(), new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "bar" }) };
            var session = new BattleSession(new GpBoard(1, 2), db, rng.Stream("battle"), slots, requiredScore: 1);

            ServeResult result = session.Serve(0);

            Assert.AreEqual(ServeOutcome.Placed, result.Outcome);
            Assert.AreEqual(1, result.Dish.Placement.Orientation.Width);
            Assert.AreEqual(2, result.Dish.Placement.Orientation.Height);
        }

        [Test]
        public void Serve_IsDeterministicForSameSeed()
        {
            var rngA = new RandomService();
            rngA.Init("same-battle");
            var rngB = new RandomService();
            rngB.Init("same-battle");

            BattleSession a = BuildSession(rngA, 1);
            BattleSession b = BuildSession(rngB, 1);

            for (int i = 0; i < 5; i++)
            {
                ServeResult ra = a.Serve(0);
                ServeResult rb = b.Serve(0);
                Assert.AreEqual(ra.Outcome, rb.Outcome, $"outcome diverged at {i}");
                if (ra.Success)
                {
                    Assert.AreEqual(ra.Dish.Def.Id, rb.Dish.Def.Id, $"dish diverged at {i}");
                    Assert.AreEqual(ra.Dish.Placement.Origin, rb.Dish.Placement.Origin, $"placement diverged at {i}");
                }
            }
        }

        [Test]
        public void Serve_EmptySlot_ReturnsSlotEmpty()
        {
            var rng = new RandomService();
            rng.Init(1UL);
            GameplayDatabase db = BuildDb();
            var slots = new List<RecipeSlot> { new RecipeSlot("empty", new string[0]) };
            var session = new BattleSession(new GpBoard(4, 4), db, rng.Stream("b"), slots, 1);

            Assert.AreEqual(ServeOutcome.SlotEmpty, session.Serve(0).Outcome);
        }

        [Test]
        public void Settle_DeterminesWinByThreshold()
        {
            var rng = new RandomService();
            rng.Init("settle-seed");
            BattleSession session = BuildSession(rng, requiredScore: 5);

            session.Serve(0); // at least one dish (>=5 deliciousness for rice/egg)
            session.Settle();

            Assert.IsTrue(session.IsSettled);
            Assert.IsTrue(session.IsWin, "single dish should already meet the low threshold");
        }

        [Test]
        public void Serve_RespectsMaxServesLimit()
        {
            var rng = new RandomService();
            rng.Init("limit-seed");
            BattleSession session = BuildSession(rng, requiredScore: 1);
            session.MaxServes = 2;

            Assert.AreEqual(ServeOutcome.Placed, session.Serve(0).Outcome);
            Assert.AreEqual(ServeOutcome.Placed, session.Serve(0).Outcome);
            Assert.AreEqual(2, session.ServesUsed);

            // 第三次上菜应被限量供应挡下。
            Assert.AreEqual(ServeOutcome.LimitReached, session.Serve(0).Outcome);
            Assert.AreEqual(2, session.Board.DishCount);
            Assert.IsFalse(session.CanServeAny());
        }

        [Test]
        public void ClearBoard_RemovesAllDishes()
        {
            var rng = new RandomService();
            rng.Init(7UL);
            BattleSession session = BuildSession(rng, 1);
            session.Serve(0);
            session.Serve(0);

            session.ClearBoard();

            Assert.AreEqual(0, session.Board.DishCount);
        }
    }
}
