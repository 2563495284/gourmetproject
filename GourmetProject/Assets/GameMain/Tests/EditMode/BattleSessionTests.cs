using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
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
            return new GameplayDatabase(
                dishes,
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
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
        public void Serve_UsesFixedOrientationWhenRotationDisallowed()
        {
            var rng = new RandomService();
            rng.Init("serve-rotate");
            DishDef bar = GameplayTestFactory.Dish("bar", new[] { "XX" }, allowRotate: false);
            var db = new GameplayDatabase(
                new[] { bar },
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "bar" }) };

            // 变体禁旋：上菜只以固定朝向(2x1)摆放，不会旋转成 1x2。
            var session = new BattleSession(new GpBoard(2, 2), db, rng.Stream("battle"), slots, requiredScore: 1);
            ServeResult result = session.Serve(0);

            Assert.AreEqual(ServeOutcome.Placed, result.Outcome);
            Assert.AreEqual(2, result.Dish.Placement.Orientation.Width);
            Assert.AreEqual(1, result.Dish.Placement.Orientation.Height);
        }

        [Test]
        public void Serve_NoFittingDish_WhenFixedOrientationCannotFit()
        {
            var rng = new RandomService();
            rng.Init("serve-norotate");
            DishDef bar = GameplayTestFactory.Dish("bar", new[] { "XX" }, allowRotate: false);
            var db = new GameplayDatabase(
                new[] { bar },
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "bar" }) };

            // 2x1 的菜在 1x2 棋盘上因禁旋无法摆放。
            var session = new BattleSession(new GpBoard(1, 2), db, rng.Stream("battle"), slots, requiredScore: 1);
            ServeResult result = session.Serve(0);

            Assert.AreEqual(ServeOutcome.NoFittingDish, result.Outcome);
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
        public void Serve_TriggersOnServeGoldRule()
        {
            var rng = new RandomService();
            rng.Init("serve-gold");
            SkillDef serveGold = GameplayTestFactory.RuleSkill("serve_gold",
                GameplayTestFactory.Rule(SkillActionType.GrantGold, 3f, trigger: SkillTrigger.OnServe));
            DishDef coin = GameplayTestFactory.Dish("coin", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "serve_gold" });
            var db = new GameplayDatabase(
                new[] { coin },
                new[] { serveGold },
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "coin" }) };
            var session = new BattleSession(new GpBoard(4, 4), db, rng.Stream("battle"), slots, requiredScore: 1);

            session.Serve(0);

            Assert.AreEqual(3f, session.PendingGold, 0.001f);
        }

        [Test]
        public void Settle_AccumulatesGoldAndSettledCounts()
        {
            var rng = new RandomService();
            rng.Init("settle-gold");
            SkillDef gold = GameplayTestFactory.RuleSkill("gold_on_settle",
                GameplayTestFactory.Rule(SkillActionType.GrantGold, 7f));
            DishDef coin = GameplayTestFactory.Dish("coin", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "gold_on_settle" });
            var db = new GameplayDatabase(
                new[] { coin },
                new[] { gold },
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "coin" }) };
            var session = new BattleSession(new GpBoard(4, 4), db, rng.Stream("battle"), slots, requiredScore: 1);

            session.Serve(0);
            session.Settle();

            Assert.AreEqual(7f, session.PendingGold, 0.001f);
            Assert.IsTrue(session.LastSettledIncrements.TryGetValue("coin", out int c) && c == 1);
        }

        [Test]
        public void Serve_AccumulatesGlobalHappyCakeLayers()
        {
            var rng = new RandomService();
            rng.Init("serve-layer");
            SkillDef addLayer = GameplayTestFactory.RuleSkill("layer_on_serve",
                GameplayTestFactory.Rule(SkillActionType.AddLayer, 5f, trigger: SkillTrigger.OnServe));
            DishDef cake = GameplayTestFactory.Dish("cake_layer", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "layer_on_serve" });
            var db = new GameplayDatabase(
                new[] { cake },
                new[] { addLayer },
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "cake_layer", "cake_layer" }) };
            var session = new BattleSession(new GpBoard(4, 4), db, rng.Stream("battle"), slots, requiredScore: 1);

            session.Serve(0);
            Assert.AreEqual(5, session.HappyCakeLayers);
            session.Serve(0);
            Assert.AreEqual(10, session.HappyCakeLayers, "全局层数在多次上菜间共享累加");
        }

        [Test]
        public void Settle_ConsumesGlobalLayersFromServe()
        {
            var rng = new RandomService();
            rng.Init("settle-layer");
            // 上菜 +6 层；结算时最多消耗 3 层。
            SkillDef addLayer = GameplayTestFactory.RuleSkill("layer_add",
                GameplayTestFactory.Rule(SkillActionType.AddLayer, 6f, trigger: SkillTrigger.OnServe));
            SkillDef consume = GameplayTestFactory.RuleSkill("layer_consume",
                GameplayTestFactory.Rule(
                    SkillActionType.ConsumeLayer, 1f,
                    condType: SkillConditionType.LayerCount, condScope: SkillScope.Self, condMode: CountMode.Per,
                    condParam: "cap:3"));
            DishDef adder = GameplayTestFactory.Dish("adder", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "layer_add" });
            DishDef eater = GameplayTestFactory.Dish("eater", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "layer_consume" });
            var db = new GameplayDatabase(
                new[] { adder, eater },
                new[] { addLayer, consume },
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
            var slots = new[] { new RecipeSlot("slot0", new[] { "adder", "eater" }) };
            var session = new BattleSession(new GpBoard(4, 4), db, rng.Stream("battle"), slots, requiredScore: 1);

            session.Serve(0);
            session.Serve(0);
            Assert.AreEqual(6, session.HappyCakeLayers);

            session.Settle();
            Assert.AreEqual(3, session.HappyCakeLayers, "cap:3 → 结算消耗 3 层，剩 3");
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

        [Test]
        public void RecipeEntryFlags_DisableSkillsAndExcludeScore()
        {
            var rng = new RandomService();
            rng.Init("entry-flags");
            SkillDef flat = GameplayTestFactory.RuleSkill("flat",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 5f));
            DishDef dish = GameplayTestFactory.Dish("dish", new[] { "X" }, deliciousness: 5, allowRotate: false, skills: new[] { "flat" });
            var db = new GameplayDatabase(new[] { dish }, new[] { flat }, new List<FlavorDef>(), new List<CellTagDef>(), new List<RecipeDef>());
            var slot = new RecipeSlot("slot0", new[] { "dish", "dish" });
            slot.Entries[0].MarkSkillsDisabled();
            slot.Entries[1].MarkExcludedFromScore();
            var session = new BattleSession(new GpBoard(2, 1), db, rng.Stream("battle"), new[] { slot }, requiredScore: 1);

            session.Serve(0);
            session.Serve(0);
            ScoreResult result = session.Settle();

            Assert.AreEqual(1, result.DishScores.Count);
            Assert.AreEqual(5, result.Total, "第一道技能失效，仅保留基础分；第二道不参与计分。");
            Assert.IsTrue(session.LastSettledIncrements.TryGetValue("dish", out int count) && count == 1);
        }

        [Test]
        public void ServeModifiers_ApplyTastingComboAppetizerAndGoldCost()
        {
            var rng = new RandomService();
            rng.Init("serve-debuffs");
            GameplayDatabase db = BuildDb();
            var slot = new RecipeSlot("slot0", new[] { "rice", "rice", "rice" });
            var session = new BattleSession(new GpBoard(3, 1), db, rng.Stream("battle"), new[] { slot }, requiredScore: 1)
            {
                AlternateServeMultiplier = true,
                AutoServeSecondDish = true,
                RemoveFirstServedDishes = true,
                FirstServedDishesToRemove = 1,
                GoldCostPerServe = 5,
            };

            ServeResult result = session.Serve(0);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, session.ServesUsed, "套餐应额外上一道菜。");
            Assert.AreEqual(1, session.Board.DishCount, "开胃菜移除第一道，套餐额外菜保留。");
            Assert.AreEqual(-10f, session.PendingGold, 0.001f);
            ScoreResult score = session.Settle();
            Assert.AreEqual(8, score.Total, "第二道菜倍率为 1.5，5 分四舍五入为 8。");
        }

        [Test]
        public void BoardDisabledCells_PreventPlacement()
        {
            var board = new GpBoard(1, 1);
            board.SetDisabled(new GridPos(0, 0), true);
            DishDef dish = GameplayTestFactory.Dish("dish", new[] { "X" }, allowRotate: false);

            Assert.IsFalse(board.CanFit(dish));
            Assert.AreEqual(0, board.EmptyCellCount);
        }

        [Test]
        public void ScoreCalculator_CanReverseSettlementOrder()
        {
            DishDef top = GameplayTestFactory.Dish("top", new[] { "X" }, deliciousness: 1, allowRotate: false);
            DishDef bottom = GameplayTestFactory.Dish("bottom", new[] { "X" }, deliciousness: 1, allowRotate: false);
            var db = new GameplayDatabase(new[] { top, bottom }, new List<SkillDef>(), new List<FlavorDef>(), new List<CellTagDef>(), new List<RecipeDef>());
            var board = new GpBoard(1, 2);
            board.Place(GameplayTestFactory.Instance(1, top, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, bottom, 0, 1));

            ScoreResult normal = new ScoreCalculator().Calculate(board, db);
            ScoreResult reverse = new ScoreCalculator().Calculate(board, db, reverseDishOrder: true);

            Assert.AreEqual("top", normal.DishScores[0].DishId);
            Assert.AreEqual("bottom", reverse.DishScores[0].DishId);
        }

        [Test]
        public void Buffet_MinimumServesForScoreZerosResult()
        {
            var rng = new RandomService();
            rng.Init("buffet");
            BattleSession session = BuildSession(rng, requiredScore: 1);
            session.MinimumServesForScore = 10;
            session.Serve(0);

            ScoreResult result = session.Settle();

            Assert.AreEqual(0, result.Total);
            Assert.IsFalse(session.IsWin);
        }
    }
}
