using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Tests
{
    /// <summary>
    /// 道具结算类效果测试：验证 ItemScoreEffectSource 各效果类型在结算管线中的行为。
    /// 纯玩法层（不依赖 cfg/Game），构造方式与 CakeLayerBuffTests 一致。
    /// </summary>
    public class ItemEffectTests
    {
        private static GameplayDatabase EmptyDb()
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                new List<SkillDef>(),
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
        }

        private static ScoreResult Calc(GpBoard board, params ItemScoreSpec[] specs)
        {
            var source = new ItemScoreEffectSource(specs);
            return new ScoreCalculator(effectSources: new IScoreEffectSource[] { source }).Calculate(board, EmptyDb());
        }

        [Test]
        public void CakeThresholdReduction_LowersEffectiveThreshold()
        {
            var db = new GameplayDatabase(
                new List<DishDef>(), new List<SkillDef>(), new List<FlavorDef>(), new List<CellTagDef>(), new List<RecipeDef>(),
                cakeLayerBuffs: new[] { new CakeLayerBuffDef("clb", 0, 50, "cake", SkillActionType.AddFlat, 1f, "") });
            var board = new GpBoard(1, 1);
            DishDef cake = GameplayTestFactory.Dish("cake", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "cake");
            board.Place(GameplayTestFactory.Instance(1, cake, 0, 0));

            // 层数 45 < 阈值 50，无下调 → 无加成。
            ScoreResult none = new ScoreCalculator().Calculate(board, db, initialHappyCakeLayers: 45);
            Assert.AreEqual(10f, none.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);

            // 阈值下调 10 → 有效阈值 40，45 >= 40 → +1×45=45 → 55。
            ScoreResult reduced = new ScoreCalculator().Calculate(board, db, initialHappyCakeLayers: 45, cakeLayerThresholdReduction: 10);
            Assert.AreEqual(55f, reduced.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        private static float Score(ScoreResult r, int dishId)
        {
            return r.DishScores.First(s => s.DishInstanceId == dishId).Contribution;
        }

        [Test]
        public void TagBonus_OnlyMatchingCategoryDishesGetFlat()
        {
            var board = new GpBoard(4, 1);
            DishDef spicy = GameplayTestFactory.Dish("spicy", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "spicy");
            DishDef plain = GameplayTestFactory.Dish("plain", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, spicy, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, plain, 1, 0));

            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.TagBonus, 10f, "spicy", 1, "item_pepper_jar", "胡椒罐"));

            Assert.AreEqual(20f, Score(r, 1), 0.001f); // 10 + 10
            Assert.AreEqual(10f, Score(r, 2), 0.001f);
        }

        [Test]
        public void TagBonus_ScalesWithLevel()
        {
            var board = new GpBoard(2, 1);
            DishDef spicy = GameplayTestFactory.Dish("spicy", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "spicy");
            board.Place(GameplayTestFactory.Instance(1, spicy, 0, 0));

            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.TagBonus, 10f, "spicy", 3, "item_pepper_jar", "胡椒罐"));

            Assert.AreEqual(40f, Score(r, 1), 0.001f); // 10 + 10*3
        }

        [Test]
        public void CountThresholdFinalMult_AppliesWhenWithinThreshold()
        {
            var board = new GpBoard(4, 1);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, d, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, d, 1, 0));

            // 数量 2 <= 10 → 最终总分 ×1.5。总分 = (10+10)*1.5 = 30。
            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.CountThresholdFinalMult, 1.5f, "lte:10", 1, "i", "i"));
            Assert.AreEqual(30f, r.Total, 0.001f);
        }

        [Test]
        public void CountThresholdFinalMult_SkipsWhenOutsideThreshold()
        {
            var board = new GpBoard(4, 1);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, d, 0, 0));

            // 数量 1，不满足 gte:10 → 无乘区。总分 = 10。
            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.CountThresholdFinalMult, 1.5f, "gte:10", 1, "i", "i"));
            Assert.AreEqual(10f, r.Total, 0.001f);
        }

        [Test]
        public void NthServeMult_FirstAndLast()
        {
            var board = new GpBoard(4, 1);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, d, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, d, 1, 0));
            board.Place(GameplayTestFactory.Instance(3, d, 2, 0));

            // 第一个（Id=1）×2 → 20。
            ScoreResult first = Calc(board, new ItemScoreSpec(ItemScoreEffectType.NthServeMult, 2f, "index:1", 1, "i", "i"));
            Assert.AreEqual(20f, Score(first, 1), 0.001f);
            Assert.AreEqual(10f, Score(first, 3), 0.001f);

            // 最后一个（Id=3）×2 → 20。
            ScoreResult last = Calc(board, new ItemScoreSpec(ItemScoreEffectType.NthServeMult, 2f, "index:-1", 1, "i", "i"));
            Assert.AreEqual(10f, Score(last, 1), 0.001f);
            Assert.AreEqual(20f, Score(last, 3), 0.001f);
        }

        [Test]
        public void PermanentAddFlatAll_AddsToEveryDish()
        {
            var board = new GpBoard(2, 1);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, d, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, d, 1, 0));

            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.PermanentAddFlatAll, 10f, "", 1, "i", "i"));
            Assert.AreEqual(20f, Score(r, 1), 0.001f);
            Assert.AreEqual(20f, Score(r, 2), 0.001f);
            // 永久加成登记为持久增量，供 BattleSession 写回实例。
            Assert.IsTrue(r.PermanentFlatDeltas.ContainsKey(1) && r.PermanentFlatDeltas.ContainsKey(2));
        }

        [Test]
        public void PermanentAddMultAll_MultipliesEveryDish()
        {
            var board = new GpBoard(2, 1);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.Instance(1, d, 0, 0));

            // 永久乘区 ×(1+0.2) → 10*1.2=12。
            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.PermanentAddMultAll, 0.2f, "", 1, "i", "i"));
            Assert.AreEqual(12f, Score(r, 1), 0.001f);
        }

        [Test]
        public void PerSkillMultFlat_AddsMultPerBoardSkill()
        {
            var board = new GpBoard(2, 1);
            DishDef withSkills = GameplayTestFactory.Dish("d1", new[] { "X" }, deliciousness: 10, allowRotate: false, skills: new[] { "s1", "s2" });
            DishDef oneSkill = GameplayTestFactory.Dish("d2", new[] { "X" }, deliciousness: 10, allowRotate: false, skills: new[] { "s3" });
            board.Place(GameplayTestFactory.Instance(1, withSkills, 0, 0));
            board.Place(GameplayTestFactory.Instance(2, oneSkill, 1, 0));

            // 总技能数 3，value 0.1 → 每道菜倍率 +0.3 → 10*1.3=13。
            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.PerSkillMultFlat, 0.1f, "", 1, "i", "i"));
            Assert.AreEqual(13f, Score(r, 1), 0.001f);
            Assert.AreEqual(13f, Score(r, 2), 0.001f);
        }

        [Test]
        public void EveryNthServeMult_EveryThreeThenNext()
        {
            var board = new GpBoard(8, 1);
            DishDef d = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            for (int i = 0; i < 7; i++)
            {
                board.Place(GameplayTestFactory.Instance(i + 1, d, i, 0));
            }

            // every:3 → 上菜序号 4、7 的菜倍率 +2（其余不变）。
            ScoreResult r = Calc(board, new ItemScoreSpec(ItemScoreEffectType.EveryNthServeMult, 2f, "every:3", 1, "i", "i"));
            Assert.AreEqual(30f, Score(r, 4), 0.001f); // 10*(1+2)
            Assert.AreEqual(30f, Score(r, 7), 0.001f);
            Assert.AreEqual(10f, Score(r, 1), 0.001f);
            Assert.AreEqual(10f, Score(r, 5), 0.001f);
        }
    }
}
