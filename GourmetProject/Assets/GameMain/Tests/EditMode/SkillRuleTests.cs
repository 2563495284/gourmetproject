using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using NUnit.Framework;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Tests
{
    /// <summary>「前提×行为」规则技能测试：前提 count 计算、各行为效果、跨菜命令、历史/菜谱/层数/金币。</summary>
    public class SkillRuleTests
    {
        private static GameplayDatabase Db(params SkillDef[] skills)
        {
            return new GameplayDatabase(
                new List<DishDef>(),
                skills,
                new List<FlavorDef>(),
                new List<CellTagDef>(),
                new List<RecipeDef>());
        }

        private static DishInstance Place(GpBoard board, int id, DishDef def, int x, int y, params string[] skills)
        {
            DishInstance inst = GameplayTestFactory.InstanceWithTags(id, def, x, y, skills);
            board.Place(inst);
            return inst;
        }

        // ---------- 无前提行为 ----------

        [Test]
        public void RuleAddFlat_NoCondition_AddsOnce()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 5f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(15f, result.RawSum, 0.001f);
        }

        [Test]
        public void RuleAddMult_WithPerCount_StacksAsPower()
        {
            // 前提：相邻每有 1 个菜 → count；行为：本菜乘区 x2 叠 count 次。
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 2f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef single = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, single, 1, 1, "s");
            Place(board, 2, single, 0, 1);
            Place(board, 3, single, 2, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 2 个相邻 → x2^2=4 → 10*4=40
            DishScore score = result.DishScores.First(s => s.DishInstanceId == 1);
            Assert.AreEqual(40f, score.Contribution, 0.001f);
        }

        [Test]
        public void RuleAddFlat_WithRoundCondition_CountsDiagonalDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Round, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1); // 共边相邻
            Place(board, 3, dish, 0, 0); // 对角相邻
            Place(board, 4, dish, 3, 3); // 范围外

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(14f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void RuleAddFlat_WithRoundAndSelfCondition_CountsSelfAndDiagonalDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.RoundAndSelf, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1); // 共边相邻
            Place(board, 3, dish, 0, 0); // 对角相邻
            Place(board, 4, dish, 3, 3); // 范围外

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void RuleAddFlat_WithOtherCondition_CountsAllExceptSelf()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Other, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);
            Place(board, 3, dish, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(14f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void RuleAddFlat_WithAdjacentCondition_IgnoresDiagonalDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 0); // 仅对角，不算 Adjacent

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- 倍率加法 AddMultFlat（倍率 +X，线性叠加，区别于乘法幂叠） ----------

        [Test]
        public void RuleAddMultFlat_NoCondition_AddsToMultiplier()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddMultFlat, 3f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 倍率 1+3=4 → 10*4=40
            Assert.AreEqual(40f, result.RawSum, 0.001f);
        }

        [Test]
        public void RuleAddMultFlat_WithPerCount_AddsLinearlyNotPower()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMultFlat, 1f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1);
            Place(board, 3, dish, 2, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 2 相邻 → 倍率 1 + 1*2 = 3 → 10*3=30（线性叠加，而非 1*2^2）
            Assert.AreEqual(30f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void AddMultFlat_ToAdjacent_AddsToNeighborMultiplier()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddMultFlat, 4f, actionScope: SkillScope.Adjacent)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f); // 自身倍率 1
            Assert.AreEqual(50f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f); // 邻居倍率 1+4=5
        }

        // ---------- 前提：空格 / 数量 / 大小 / 形状 / 相同 ----------

        [Test]
        public void EmptyCell_ScalesWithBoardEmpty()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.EmptyCell, condScope: SkillScope.All, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 9, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(39f, result.RawSum, 0.001f); // 9 + 2*15
        }

        [Test]
        public void DishSize_CountsBigDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 1f,
                    condType: SkillConditionType.DishSize, condScope: SkillScope.All, condMode: CountMode.Per,
                    condCompare: CompareOp.Gte, condThreshold: 2)));
            var board = new GpBoard(4, 4);
            DishDef small = GameplayTestFactory.Dish("s1", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef big = GameplayTestFactory.Dish("b1", new[] { "XX" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, small, 0, 0, "s");
            Place(board, 2, big, 0, 2);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(11f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void ShapeMatch_CountsMatchingShape()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 1f,
                    condType: SkillConditionType.ShapeMatch, condScope: SkillScope.All, condMode: CountMode.Per,
                    condParam: "1x1")));
            var board = new GpBoard(4, 4);
            DishDef single = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, single, 0, 0, "s");
            Place(board, 2, single, 1, 0);
            Place(board, 3, single, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f); // 全场 3 个 1x1（含自身）
        }

        [Test]
        public void SameDish_CountsSameBaseId()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 3f,
                    condType: SkillConditionType.SameDish, condScope: SkillScope.All, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef other = GameplayTestFactory.Dish("o", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);
            Place(board, 3, other, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- 前提：闸门 / 阶梯 ----------

        [Test]
        public void Reach_GateOpensWhenThresholdMet()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 100f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.All, condMode: CountMode.Reach,
                    condCompare: CompareOp.Gte, condThreshold: 2)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);
            Place(board, 3, dish, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 全场 3 个菜 >= 2 → 闸门开 → +100
            Assert.AreEqual(110f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void Gate_ClosedProducesNoEffect()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 5f,
                    condType: SkillConditionType.PositionFilled, condScope: SkillScope.Row, condMode: CountMode.Gate)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s"); // 同行未填满

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void PositionFilled_RowFullOpensGate()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 2f,
                    condType: SkillConditionType.PositionFilled, condScope: SkillScope.Row, condMode: CountMode.Gate)));
            var board = new GpBoard(2, 1); // 2 格一行
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance self = Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(20f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- 跨菜行为 ----------

        [Test]
        public void AddFlat_ToAdjacent_AffectsNeighborNotSelf()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.Adjacent)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
        }

        [Test]
        public void AddFlat_ToRound_AffectsSideAndDiagonalTargets()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.Round)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1); // 共边相邻
            Place(board, 3, dish, 0, 0); // 对角相邻
            Place(board, 4, dish, 3, 3); // 范围外

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 4).Contribution, 0.001f);
        }

        [Test]
        public void AddFlat_ToRoundAndSelf_AffectsSelfSideAndDiagonalTargets()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.RoundAndSelf)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1); // 共边相邻
            Place(board, 3, dish, 0, 0); // 对角相邻
            Place(board, 4, dish, 3, 3); // 范围外

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 4).Contribution, 0.001f);
        }

        [Test]
        public void AddFlat_ToRowAndSelf_AffectsSameRowAndSelf()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.RowAndSelf)));
            var board = new GpBoard(3, 3);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 0, 1); // 同行
            Place(board, 3, dish, 1, 0); // 同列但不同行

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f);
        }

        [Test]
        public void AddFlat_ToColumnAndSelf_AffectsSameColumnAndSelf()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.ColumnAndSelf)));
            var board = new GpBoard(3, 3);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 1, 1, "s");
            Place(board, 2, dish, 1, 0); // 同列
            Place(board, 3, dish, 0, 1); // 同行但不同列

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(13f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f);
        }

        [Test]
        public void AddFlat_ToCakeBuff_DoesNotTargetDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 3f, actionScope: SkillScope.CakeBuff)));
            var board = new GpBoard(4, 4);
            DishDef source = GameplayTestFactory.Dish("src", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef cake = GameplayTestFactory.Dish("cake", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "cake");
            DishDef other = GameplayTestFactory.Dish("other", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "drink");
            Place(board, 1, source, 0, 0, "s");
            Place(board, 2, cake, 1, 0);
            Place(board, 3, other, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f);
        }

        [Test]
        public void TransferScore_MovesFlatBetweenDishes()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.TransferScore, 0.5f, actionScope: SkillScope.All)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(5f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(15f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        [Test]
        public void ExtraSettlement_CountsContributionTwice()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.ExtraSettlement, 1f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(20f, result.RawSum, 0.001f);
        }

        // ---------- 副作用：金币 / 层数 ----------

        [Test]
        public void GrantGold_AccumulatesGoldDelta()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.GrantGold, 5f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.All, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.GoldDelta, 0.001f); // 全场 2 个菜（含自身）→ 5*2
        }

        [Test]
        public void LayerCount_ReadsGlobalHappyCakeLayers()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.LayerCount, condScope: SkillScope.Self, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            // 全局层数 3 注入 → count=3 → 10 + 2*3 = 16
            ScoreResult result = new ScoreCalculator().Calculate(board, db, initialHappyCakeLayers: 3);

            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void LayerCount_CapLimitsRawValue()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.LayerCount, condScope: SkillScope.Self, condMode: CountMode.Per,
                    condParam: "cap:3")));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            // 全局层数 5，但 cap:3 → count=3 → 10 + 2*3 = 16
            ScoreResult result = new ScoreCalculator().Calculate(board, db, initialHappyCakeLayers: 5);

            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void AddLayer_ProducesGlobalLayerDelta()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddLayer, 2f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(2, result.HappyCakeLayerDelta);
        }

        [Test]
        public void ConsumeLayer_ReducesGlobalLayers()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.ConsumeLayer, 3f)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db, initialHappyCakeLayers: 5);

            Assert.AreEqual(-3, result.HappyCakeLayerDelta); // 5 → 2
        }

        // ---------- 历史 / 菜谱 ----------

        [Test]
        public void SameKindInRun_ReadsRunHistory()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 2f,
                    condType: SkillConditionType.SameKindInRun, condScope: SkillScope.Self, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            var history = new ScoreHistory(
                new Dictionary<string, int> { ["d"] = 3 },
                new Dictionary<string, int>(),
                new List<string>());

            ScoreResult result = new ScoreCalculator().Calculate(board, db, history: history);

            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void RecipeCount_GateFromRecipe()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 5f,
                    condType: SkillConditionType.RecipeCount, condScope: SkillScope.Recipe, condMode: CountMode.Gate,
                    condCompare: CompareOp.Gte, condThreshold: 3)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            var history = new ScoreHistory(
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                new List<string> { "a", "a", "b" });

            ScoreResult result = new ScoreCalculator().Calculate(board, db, history: history);

            Assert.AreEqual(15f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- P3：视为 N 个食物 ----------

        [Test]
        public void CountAs_StaticDefValue_CountsAsMultipleForNeighbor()
        {
            // 邻居技能：相邻每有 1 个食物 +10 分；相邻是一道「视为 3」的菜 → 记 3 个。
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 10f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condMode: CountMode.Per)));
            var board = new GpBoard(4, 4);
            DishDef counter = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 0, allowRotate: false);
            DishDef triple = GameplayTestFactory.Dish("t", new[] { "X" }, deliciousness: 0, allowRotate: false, countAs: 3);
            Place(board, 1, counter, 1, 1, "s");
            Place(board, 2, triple, 0, 1);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 1 个相邻实例但视为 3 → 10*3 = 30
            Assert.AreEqual(30f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void CountAs_ConditionalSelf_AddsCountAsWhenGateOpen()
        {
            // 自身：同行填满 → 视为 +9（基础 1 → 10）；邻居 counter：相邻每有 1 个食物 +1 分。
            var selfSkill = GameplayTestFactory.RuleSkill("cas",
                GameplayTestFactory.Rule(
                    SkillActionType.AddCountAs, 9f,
                    condType: SkillConditionType.PositionFilled, condScope: SkillScope.Row, condMode: CountMode.Gate,
                    actionScope: SkillScope.Self));
            var counterSkill = GameplayTestFactory.RuleSkill("cnt",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 1f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Adjacent, condMode: CountMode.Per));
            GameplayDatabase db = Db(selfSkill, counterSkill);
            var board = new GpBoard(2, 1); // 一行 2 格，摆满即填满
            DishDef counter = GameplayTestFactory.Dish("c", new[] { "X" }, deliciousness: 0, allowRotate: false);
            DishDef waffle = GameplayTestFactory.Dish("w", new[] { "X" }, deliciousness: 0, allowRotate: false);
            Place(board, 1, counter, 0, 0, "cnt");
            Place(board, 2, waffle, 1, 0, "cas");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // waffle 同行填满 → 视为 10；counter 相邻 1 个实例视为 10 → +10
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void CountAs_ColumnGrant_BoostsSameColumnCount()
        {
            // dorayaki：同列食物额外视为 +2；counter：同列每有 1 个食物 +1 分。
            var dora = GameplayTestFactory.RuleSkill("dora",
                GameplayTestFactory.Rule(SkillActionType.AddCountAs, 2f, actionScope: SkillScope.Column));
            var counterSkill = GameplayTestFactory.RuleSkill("cnt",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 1f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.Column, condMode: CountMode.Per));
            GameplayDatabase db = Db(dora, counterSkill);
            var board = new GpBoard(1, 3); // 一列 3 格
            DishDef counter = GameplayTestFactory.Dish("c", new[] { "X" }, deliciousness: 0, allowRotate: false);
            DishDef doraDish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 0, allowRotate: false);
            Place(board, 1, counter, 0, 0, "cnt");
            Place(board, 2, doraDish, 0, 1, "dora");
            Place(board, 3, counter, 0, 2, "cnt");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // counter#1 同列其它 2 道：dora(视为1+2=3) + counter#3(视为1) = 4 → +4
            Assert.AreEqual(4f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- P3：永久分（跨结算累积） ----------

        [Test]
        public void PermanentAddFlat_AppliesThisSettleAndPersistsAcrossSettles()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.PermanentAddFlat, 5f)));
            var board = new GpBoard(4, 4);
            var rng = new GourmetProject.Core.Rng.RandomService();
            rng.Init("perm-flat");
            var slots = new List<RecipeSlot> { new RecipeSlot("slot", new string[0]) };
            var session = new BattleSession(board, db, rng.Stream("b"), slots, requiredScore: 0);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "s" }));

            ScoreResult first = session.Settle();
            ScoreResult second = session.Settle();

            // 第一次：基础 10 + 永久 5（本次即生效） = 15
            Assert.AreEqual(15f, first.RawSum, 0.001f);
            // 第二次：基础 10 + 已持久 5 + 本次再 +5 = 20
            Assert.AreEqual(20f, second.RawSum, 0.001f);
        }

        [Test]
        public void PermanentAddMult_MultipliesThisSettleAndPersists()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.PermanentAddMult, 2f)));
            var board = new GpBoard(4, 4);
            var rng = new GourmetProject.Core.Rng.RandomService();
            rng.Init("perm-mult");
            var slots = new List<RecipeSlot> { new RecipeSlot("slot", new string[0]) };
            var session = new BattleSession(board, db, rng.Stream("b"), slots, requiredScore: 0);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            board.Place(GameplayTestFactory.InstanceWithTags(1, dish, 0, 0, new[] { "s" }));

            ScoreResult first = session.Settle();
            ScoreResult second = session.Settle();

            // 第一次：10 × 2 = 20
            Assert.AreEqual(20f, first.RawSum, 0.001f);
            // 第二次：乘区初值已持久 ×2，本次再 ×2 → 10 × 4 = 40
            Assert.AreEqual(40f, second.RawSum, 0.001f);
        }

        // ---------- P4：蛋糕分类（CategoryCount 前提 + Category 定向） ----------

        [Test]
        public void CategoryCount_CountsCakesOnly()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat, 3f,
                    condType: SkillConditionType.CategoryCount, condMode: CountMode.Per, condParam: "cake")));
            var board = new GpBoard(4, 4);
            DishDef cake = GameplayTestFactory.Dish("ck", new[] { "X" }, deliciousness: 0, allowRotate: false, category: "cake");
            DishDef plain = GameplayTestFactory.Dish("pl", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, plain, 0, 0, "s");
            Place(board, 2, cake, 1, 0);
            Place(board, 3, cake, 2, 0);
            Place(board, 4, plain, 3, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 场上 2 个 cake → 10 + 3*2 = 16
            Assert.AreEqual(16f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void AddMultFlat_CategoryScope_AddsToAllCakes()
        {
            // 戚风式：将倍率 +5 加到所有蛋糕（含自身若为蛋糕），非蛋糕不受影响。
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMultFlat, 5f,
                    actionScope: SkillScope.Category, actionParam: "cat:cake")));
            var board = new GpBoard(4, 4);
            DishDef chiffon = GameplayTestFactory.Dish("cf", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "cake");
            DishDef cake = GameplayTestFactory.Dish("ck", new[] { "X" }, deliciousness: 10, allowRotate: false, category: "cake");
            DishDef plain = GameplayTestFactory.Dish("pl", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, chiffon, 0, 0, "s");
            Place(board, 2, cake, 1, 0);
            Place(board, 3, plain, 2, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(60f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f); // 蛋糕自身 10*(1+5)
            Assert.AreEqual(60f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f); // 另一蛋糕
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f); // 非蛋糕不变
        }

        // ---------- P4：技能复制（CopySkill 候选池，OnServe） ----------

        [Test]
        public void CopySkill_FromAdjacent_GathersNeighborSkillsAsCandidates()
        {
            var copySkill = GameplayTestFactory.RuleSkill("copy",
                GameplayTestFactory.Rule(SkillActionType.CopySkill, 1f, actionScope: SkillScope.Adjacent, trigger: SkillTrigger.OnServe));
            var scoreSkill = GameplayTestFactory.RuleSkill("score",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 5f));
            GameplayDatabase db = Db(copySkill, scoreSkill);
            var board = new GpBoard(2, 1);
            DishDef neighbor = GameplayTestFactory.Dish("n", new[] { "X" }, deliciousness: 0, allowRotate: false);
            DishDef cupcake = GameplayTestFactory.Dish("c", new[] { "X" }, deliciousness: 0, allowRotate: false);
            Place(board, 1, neighbor, 0, 0, "score");
            DishInstance served = Place(board, 2, cupcake, 1, 0, "copy");

            ServeRuleResolver.ServeResolveResult res =
                ServeRuleResolver.ResolveOnServe(board, db, null, served, 0);

            Assert.AreEqual(1, res.CopySkillRequests.Count);
            Assert.AreEqual(2, res.CopySkillRequests[0].TargetInstanceId);
            Assert.AreEqual(1, res.CopySkillRequests[0].Count);
            CollectionAssert.Contains(res.CopySkillRequests[0].Candidates, "score");
        }

        [Test]
        public void CopySkill_FromCakePool_GathersCakeCategorySkills()
        {
            // 双层蛋糕式：候选池来自数据库中所有 cake 分类菜品的技能。
            var doubleCakeSkill = GameplayTestFactory.RuleSkill("double",
                GameplayTestFactory.Rule(SkillActionType.CopySkill, 3f, actionScope: SkillScope.Self, actionParam: "cat:cake", trigger: SkillTrigger.OnServe));
            var cakeSkillA = GameplayTestFactory.RuleSkill("cakeA", GameplayTestFactory.Rule(SkillActionType.AddFlat, 1f));
            var cakeSkillB = GameplayTestFactory.RuleSkill("cakeB", GameplayTestFactory.Rule(SkillActionType.AddMult, 2f));
            DishDef cakeDefA = GameplayTestFactory.Dish("cakeDefA", new[] { "X" }, category: "cake", skills: new[] { "cakeA" });
            DishDef cakeDefB = GameplayTestFactory.Dish("cakeDefB", new[] { "X" }, category: "cake", skills: new[] { "cakeB" });
            DishDef doubleDef = GameplayTestFactory.Dish("d", new[] { "X" }, category: "cake", skills: new[] { "double" });
            var db = new GameplayDatabase(
                new List<DishDef> { cakeDefA, cakeDefB, doubleDef },
                new List<SkillDef> { doubleCakeSkill, cakeSkillA, cakeSkillB },
                new List<FlavorDef>(), new List<CellTagDef>(), new List<RecipeDef>());
            var board = new GpBoard(2, 1);
            DishInstance served = Place(board, 1, doubleDef, 0, 0, "double");

            ServeRuleResolver.ServeResolveResult res =
                ServeRuleResolver.ResolveOnServe(board, db, null, served, 0);

            Assert.AreEqual(1, res.CopySkillRequests.Count);
            Assert.AreEqual(3, res.CopySkillRequests[0].Count);
            // 候选含另两种蛋糕技能，且不含自身 double（自身技能被剔除）。
            CollectionAssert.Contains(res.CopySkillRequests[0].Candidates, "cakeA");
            CollectionAssert.Contains(res.CopySkillRequests[0].Candidates, "cakeB");
            CollectionAssert.DoesNotContain(res.CopySkillRequests[0].Candidates, "double");
        }

        // ---------- P5：阶梯倍率 ----------

        [Test]
        public void Tiers_PicksHighestSatisfiedTierValue()
        {
            // tiers:2|4 阈值，tiervals:2|3 各档值；全场 5 个食物 → 达第 2 档 → 倍率 ×3。
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 0f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.All, condMode: CountMode.Reach,
                    condParam: "tiers:2|4", actionParam: "tiervals:2|3")));
            var board = new GpBoard(5, 1);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0);
            Place(board, 3, dish, 2, 0);
            Place(board, 4, dish, 3, 0);
            Place(board, 5, dish, 4, 0);

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(30f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        [Test]
        public void Tiers_BelowLowestThresholdNoEffect()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 0f,
                    condType: SkillConditionType.DishCount, condScope: SkillScope.All, condMode: CountMode.Reach,
                    condParam: "tiers:5|15|25", actionParam: "tiervals:1.5|2.5|5")));
            var board = new GpBoard(5, 1);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");
            Place(board, 2, dish, 1, 0); // 全场 2 个食物 < 5 → 无效果

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- P5：带某行为类食物数（SkillTypeCount）+ 此类食物定向 ----------

        [Test]
        public void SkillTypeCount_CountsDishesWithTransferAndBoostsThem()
        {
            var transfer = GameplayTestFactory.RuleSkill("tf",
                GameplayTestFactory.Rule(SkillActionType.TransferSkills, 0f, actionScope: SkillScope.Row, trigger: SkillTrigger.OnServe));
            var choco = GameplayTestFactory.RuleSkill("choco",
                GameplayTestFactory.Rule(
                    SkillActionType.AddMult, 0f,
                    condType: SkillConditionType.SkillTypeCount, condUnit: CountUnit.Kinds, condMode: CountMode.Reach,
                    condParam: "TransferSkills;tiers:2", actionScope: SkillScope.All,
                    actionParam: "tiervals:2;skilltype:TransferSkills"));
            GameplayDatabase db = Db(transfer, choco);
            var board = new GpBoard(4, 1);
            DishDef a = GameplayTestFactory.Dish("a", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef b = GameplayTestFactory.Dish("b", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef c = GameplayTestFactory.Dish("c", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, a, 0, 0, "tf");   // 带甜蜜传递
            Place(board, 2, b, 1, 0, "tf");   // 带甜蜜传递（不同 base）
            Place(board, 3, c, 2, 0, "choco"); // 巧克力，无传递

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 2 种带传递食物 ≥2 档 → 此类食物（a、b）×2；巧克力 c 不受影响。
            Assert.AreEqual(20f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
            Assert.AreEqual(20f, result.DishScores.First(s => s.DishInstanceId == 2).Contribution, 0.001f);
            Assert.AreEqual(10f, result.DishScores.First(s => s.DishInstanceId == 3).Contribution, 0.001f);
        }

        [Test]
        public void SkillCount_IncludesTransferredSkills()
        {
            var counter = GameplayTestFactory.RuleSkill("counter",
                GameplayTestFactory.Rule(
                    SkillActionType.AddFlat,
                    5f,
                    condType: SkillConditionType.SkillCount,
                    condMode: CountMode.Per));
            SkillRuleDef transferredRule = GameplayTestFactory.Rule(SkillActionType.AddFlat, 0f, skillId: "foreign");
            var foreign = GameplayTestFactory.RuleSkill("foreign", transferredRule);
            GameplayDatabase db = Db(counter, foreign);
            var board = new GpBoard(1, 1);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishInstance inst = Place(board, 1, dish, 0, 0, "counter");
            inst.AddTransferredSkill(new SkillEffect(transferredRule, string.Empty), "source<甜蜜传递>");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            // 1 个自身技能 + 1 个甜蜜传递外来子技能 → SkillCount=2，分数 10 + 5*2。
            Assert.AreEqual(20f, result.DishScores.First(s => s.DishInstanceId == 1).Contribution, 0.001f);
        }

        // ---------- P5：临时复制 ----------

        [Test]
        public void TempCopyDish_ClonesSelfIntoEmptyCell()
        {
            var coneCopy = GameplayTestFactory.RuleSkill("cone_copy",
                GameplayTestFactory.Rule(SkillActionType.TempCopyDish, 1f, actionScope: SkillScope.Self, trigger: SkillTrigger.OnServe));
            DishDef cone = GameplayTestFactory.Dish("cone", new[] { "X" }, deliciousness: 20, allowRotate: false, skills: new[] { "cone_copy" });
            var db = new GameplayDatabase(
                new List<DishDef> { cone },
                new List<SkillDef> { coneCopy },
                new List<FlavorDef>(), new List<CellTagDef>(), new List<RecipeDef>());
            var rng = new GourmetProject.Core.Rng.RandomService();
            rng.Init("tempcopy");
            var slots = new List<RecipeSlot> { new RecipeSlot("slot", new[] { "cone" }) };
            var session = new BattleSession(new GpBoard(2, 1), db, rng.Stream("b"), slots, requiredScore: 0);

            session.Serve(0);

            // 上菜 1 个甜筒，临时复制克隆 1 个 → 棋盘 2 个，其一为临时。
            Assert.AreEqual(2, session.Board.Dishes.Count);
            Assert.AreEqual(1, session.Board.Dishes.Count(d => d.IsTemporary));

            session.ClearTemporaryDishes();
            Assert.AreEqual(1, session.Board.Dishes.Count);
            Assert.IsFalse(session.Board.Dishes.Any(d => d.IsTemporary));
        }

        // ---------- 甜蜜传递：RNG 均权随机 + 技能来源溯源 ----------

        [Test]
        public void Transfer_ResolveOnServe_CollectsRequestWithCandidatesAndSiblingEffects()
        {
            // 一菜一 skill：马卡龙 sk_macaron 含计分子技能(AddFlat 8) + 甜蜜传递子技能(同行取1)。
            // OnServe 只收集请求、不直接落地；传递载荷是「同 skill 内其他子技能」（计分），不含传递本身。
            var macaronSkill = GameplayTestFactory.RuleSkill("sk_macaron",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 8f, order: 0),
                GameplayTestFactory.Rule(SkillActionType.TransferSkills, 0f, actionScope: SkillScope.Row, actionCount: 1, trigger: SkillTrigger.OnServe, order: 1, skillId: "sk_macaron"));
            GameplayDatabase db = Db(macaronSkill);
            var board = new GpBoard(3, 1);
            DishDef macaron = GameplayTestFactory.Dish("macaron", new[] { "X" }, deliciousness: 8, allowRotate: false);
            DishDef plain = GameplayTestFactory.Dish("plain", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, plain, 0, 0);
            Place(board, 2, plain, 2, 0);
            DishInstance served = Place(board, 3, macaron, 1, 0, "sk_macaron");

            ServeRuleResolver.ServeResolveResult res =
                ServeRuleResolver.ResolveOnServe(board, db, null, served, 0);

            Assert.AreEqual(1, res.TransferRequests.Count);
            SkillTransferRequest req = res.TransferRequests[0];
            Assert.AreEqual(3, req.SourceInstanceId);
            Assert.AreEqual("macaron", req.SourceName);
            Assert.AreEqual(1, req.Count);
            // 载荷只含「同 skill 内其他子技能」= 计分子技能一条（传递本身被排除，故为 1）。
            Assert.AreEqual(1, req.Effects.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, req.CandidateTargetIds);   // 同行两个候选，不含自身
        }

        [Test]
        public void Transfer_Serve_LandsSiblingEffectWithSourceLabelAndScores()
        {
            // 端到端：2x1 棋盘先上一个空技能目标，再上带甜蜜传递的马卡龙；同行必落地到目标。
            var macaronSkill = GameplayTestFactory.RuleSkill("sk_macaron",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 8f, order: 0),
                GameplayTestFactory.Rule(SkillActionType.TransferSkills, 0f, actionScope: SkillScope.Row, actionCount: 1, trigger: SkillTrigger.OnServe, order: 1, skillId: "sk_macaron"));
            DishDef target = GameplayTestFactory.Dish("target", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef macaron = GameplayTestFactory.Dish("macaron", new[] { "X" }, deliciousness: 8, allowRotate: false, skills: new[] { "sk_macaron" });
            var db = new GameplayDatabase(
                new List<DishDef> { target, macaron },
                new List<SkillDef> { macaronSkill },
                new List<FlavorDef>(), new List<CellTagDef>(), new List<RecipeDef>());
            var rng = new GourmetProject.Core.Rng.RandomService();
            rng.Init("transfer");
            var slots = new List<RecipeSlot>
            {
                new RecipeSlot("s0", new[] { "target" }),
                new RecipeSlot("s1", new[] { "macaron" }),
            };
            var session = new BattleSession(new GpBoard(2, 1), db, rng.Stream("b"), slots, requiredScore: 0);

            session.Serve(0); // 目标先落地
            session.Serve(1); // 马卡龙落地并把计分子技能传给目标

            DishInstance targetInst = session.Board.Dishes.First(d => d.Def.Id == "target");
            Assert.AreEqual(1, targetInst.TransferredSkills.Count);
            Assert.AreEqual(SkillActionType.AddFlat, targetInst.TransferredSkills[0].Rule.ActionType);
            Assert.AreEqual("macaron<甜蜜传递>", targetInst.TransferredSkills[0].SourceLabel);

            // 结算：目标获得并结算外来计分子技能（+8）→ 10+8=18；来源明细以来源标签命名。
            ScoreResult result = session.Settle();
            DishScore targetScore = result.DishScores.First(s => s.DishInstanceId == targetInst.Id);
            Assert.AreEqual(18f, targetScore.Contribution, 0.001f);
            Assert.IsTrue(
                result.ScoreLines.Any(l => l.DishInstanceId == targetInst.Id && l.Source != null && l.Source.Name == "macaron<甜蜜传递>"),
                "结算明细应含来源标签「macaron<甜蜜传递>」");
        }

        [Test]
        public void BigLollipop_ResolveOnServe_TriggersRowColumnSweetTransfers()
        {
            // 大棒棒糖：同行同列带甜蜜传递的食物，各执行一次它们自己的甜蜜传递。
            // 这里 source 与大棒棒糖同列；source 的甜蜜传递规则是传给同行所有目标。
            var sourceSkill = GameplayTestFactory.RuleSkill("sk_source",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 8f, order: 0, skillId: "sk_source"),
                GameplayTestFactory.Rule(
                    SkillActionType.TransferSkills,
                    0f,
                    actionScope: SkillScope.Row,
                    actionCount: 0,
                    trigger: SkillTrigger.OnServe,
                    order: 1,
                    skillId: "sk_source"));
            var bigSkill = GameplayTestFactory.RuleSkill("sk_big_lollipop",
                GameplayTestFactory.Rule(
                    SkillActionType.TriggerSweetTransfer,
                    0f,
                    actionScope: SkillScope.All,
                    actionParam: "axis:rowcol;skilltype:TransferSkills",
                    trigger: SkillTrigger.OnServe,
                    skillId: "sk_big_lollipop"));
            var plainSkill = GameplayTestFactory.RuleSkill("sk_plain",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 2f, skillId: "sk_plain"));
            GameplayDatabase db = Db(sourceSkill, bigSkill, plainSkill);
            var board = new GpBoard(3, 3);
            DishDef source = GameplayTestFactory.Dish("source", new[] { "X" }, deliciousness: 8, allowRotate: false);
            DishDef target = GameplayTestFactory.Dish("target", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef plain = GameplayTestFactory.Dish("plain", new[] { "X" }, deliciousness: 10, allowRotate: false);
            DishDef big = GameplayTestFactory.Dish("big", new[] { "X" }, deliciousness: 100, allowRotate: false);
            Place(board, 1, source, 1, 0, "sk_source"); // 与大棒棒糖同列，带甜蜜传递
            Place(board, 2, target, 2, 0);              // source 自身甜蜜传递的同行目标
            Place(board, 3, plain, 0, 1, "sk_plain");   // 与大棒棒糖同行，但不带甜蜜传递，应被忽略
            DishInstance served = Place(board, 4, big, 1, 1, "sk_big_lollipop");

            ServeRuleResolver.ServeResolveResult res =
                ServeRuleResolver.ResolveOnServe(board, db, null, served, 0);

            Assert.AreEqual(1, res.TransferRequests.Count);
            SkillTransferRequest req = res.TransferRequests[0];
            Assert.AreEqual(1, req.SourceInstanceId);
            Assert.AreEqual("source", req.SourceName);
            Assert.AreEqual(0, req.Count); // source 的原甜蜜传递规则：同行全部目标
            Assert.AreEqual(1, req.Effects.Count);
            Assert.AreEqual(SkillActionType.AddFlat, req.Effects[0].Rule.ActionType);
            CollectionAssert.AreEquivalent(new[] { 2 }, req.CandidateTargetIds);
        }

        // ---------- 多条规则组合 ----------

        [Test]
        public void MultipleRules_ActionPlusAction()
        {
            GameplayDatabase db = Db(GameplayTestFactory.RuleSkill("s",
                GameplayTestFactory.Rule(SkillActionType.AddFlat, 5f, order: 0),
                GameplayTestFactory.Rule(SkillActionType.AddMult, 2f, order: 1)));
            var board = new GpBoard(4, 4);
            DishDef dish = GameplayTestFactory.Dish("d", new[] { "X" }, deliciousness: 10, allowRotate: false);
            Place(board, 1, dish, 0, 0, "s");

            ScoreResult result = new ScoreCalculator().Calculate(board, db);

            Assert.AreEqual(30f, result.RawSum, 0.001f); // (10+5)*2
        }
    }
}
